using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// 时间异常生命周期管理：启动时回查事件日志 + 订阅实时事件；接收 HeartbeatService
/// 的检测回调（可疑 → NTP 确认）、Confirmed 通知及启动自动修复。
/// </summary>
public class TimeAnomalyService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settings;
    private readonly NtpSyncService _ntp;
    private readonly SystemTimeChangeReader _logReader;
    private readonly ITimeAnomalyNotifier? _notifier;
    private readonly ILogger<TimeAnomalyService> _logger;
    private IDisposable? _watcher;

    public TimeAnomalyService(IServiceScopeFactory scopeFactory, SettingsService settings,
        NtpSyncService ntp, SystemTimeChangeReader logReader,
        ITimeAnomalyNotifier? notifier, ILogger<TimeAnomalyService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _ntp = ntp;
        _logReader = logReader;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _watcher = _logReader.StartWatching(c => _ = IngestWatchedChangeAsync(c, stoppingToken));
        }
        catch (Exception ex) { _logger.LogError(ex, "Event-log watcher startup failed"); }

        TimeReferenceResult? startupNtp = null;
        try
        {
            if (_settings.Settings.UseNtp)
                startupNtp = await _ntp.RequestCheckAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Startup time-reference query failed");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = await GetLastKnownTimeAsync(db);
            var changes = _logReader.GetChangesSince(since);
            // A time reference queried now cannot prove whether an old, offline
            // event-log change was a correction or an intentional clock change.
            // Keep backfilled events Pending for explicit review. Only the live
            // watcher pairs an event with a fresh reference result.
            await IngestEventLogChanges(changes, ntp: null, db);
        }
        catch (Exception ex) { _logger.LogError(ex, "Startup event-log back-query failed"); }

        // Subscribe after startup backfill so the startup NTP result cannot
        // promote every historical Pending event at once. Historical rows are
        // instead closed as Resolved by the freshness guard below.
        _ntp.ResultArrived += OnNtpResultArrived;

        // Resolve stale/unverified rows before auto-apply. Older versions wrote
        // large heartbeat deltas straight as Confirmed; applying those before a
        // fresh reference pass would preserve the false-positive bug forever.
        if (startupNtp is { Succeeded: true })
            await ReevaluateUnconfirmedWithNtpAsync();

        // 启动自动应用：方向可自动判定的 Confirmed 异常（Heartbeat + EventLog/NTP 已定方向）
        // 立即修复数据；单条失败不中断（ApplyAllAutoAsync 内部逐条 try/catch）。
        try { await ApplyAllAutoAsync(startupNtp is { Succeeded: true } ? startupNtp : null); }
        catch (Exception ex) { _logger.LogError(ex, "Startup auto-apply failed"); }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void OnNtpResultArrived(TimeReferenceResult result)
    {
        try { _ = ReevaluateUnconfirmedWithNtpAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "NTP result handler failed"); }
    }

    private async Task IngestSafeAsync(IReadOnlyList<SystemTimeChange> changes, TimeReferenceResult? ntp)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await IngestEventLogChanges(changes, ntp, db);
        }
        catch (Exception ex) { _logger.LogError(ex, "Event-log watcher ingestion failed"); }
    }

    private async Task IngestWatchedChangeAsync(SystemTimeChange change, CancellationToken cancellationToken)
    {
        TimeReferenceResult? result = null;
        try
        {
            if (_settings.Settings.UseNtp)
                result = await _ntp.RequestCheckAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Time-reference query for clock-change event failed");
        }

        await IngestSafeAsync(new[] { change }, result is { Succeeded: true } ? result : null);
    }

    public async Task IngestEventLogChanges(IReadOnlyList<SystemTimeChange> changes,
        TimeReferenceResult? ntp, AppDbContext db)
    {
        foreach (var c in changes)
        {
            var delta = (c.NewTime - c.OldTime).TotalSeconds;
            // Kernel-General event 1 also records routine sub-second/seconds
            // synchronizations. The same user setting that guards heartbeat
            // repair must guard event-log ingestion as well; otherwise every
            // routine adjustment becomes a full-database repair journal.
            if (Math.Abs(delta) < _settings.Settings.TimeAnomalyThresholdSeconds)
                continue;

            // 防重复摄入：重启回查（GetChangesSince(GetLastKnownTimeAsync)）会与上一会话
            // watcher 重叠摄入 [lastHeartbeat, shutdown] 窗口内的事件，同一 Kernel-General
            // 事件被摄入两次 → TimeAnomalies 重复行 → ApplyAllAutoAsync 对同一段应用两次
            // 偏移 → 静默数据损坏。同源同 Old/NewTime 已存在则跳过（事件日志行的
            // OldTime/NewTime 恒非空，DetectedAt 保存事件发生时间）。
            var occurredAtUtc = c.OccurredAt.ToUniversalTime();
            var already = await db.TimeAnomalies.AnyAsync(a =>
                a.Source == TimeAnomalySources.EventLog
                && a.OldTime == c.OldTime
                && a.NewTime == c.NewTime
                && a.DetectedAt == occurredAtUtc);
            if (already) continue;

            string status; string? note; string? direction;
            if (ntp is { Succeeded: true })
            {
                // A successful query is not automatically corroboration. It
                // must say either "the new clock is now correct" (pre) or
                // "the new clock is still wrong by this exact delta" (post).
                direction = ReferenceDirection(delta, ntp.OffsetSeconds,
                    _settings.Settings.NtpEpsilonSeconds);
                status = direction == null
                    ? TimeAnomalyStatus.Resolved
                    : TimeAnomalyStatus.Confirmed;
                note = direction switch
                {
                    "pre" => "变化前记录错（NTP 证实本次变化为校正）",
                    "post" => "变化后记录错（NTP 偏移与本次变化吻合）",
                    _ => $"NTP 未证实本次时间变化（{ntp.SourceName}），已解除异常"
                };
            }
            else
            {
                status = TimeAnomalyStatus.Pending;
                direction = null;
                note = "无 NTP 参照，需选择修复方向";
            }
            var anomaly = new TimeAnomaly
            {
                // DetectedAt 取变化发生时刻（而非摄入时刻）——否则启动回查
                // 给几十天前的每条变更都打"刚刚检测到"，DetectedAt 过旧抑制无法生效，
                // 面板排序/清理也会受启动瞬间的批量摄入影响。
                DetectedAt = occurredAtUtc,
                Source = TimeAnomalySources.EventLog,
                OldTime = c.OldTime, NewTime = c.NewTime,
                OffsetSeconds = delta,
                Status = status,
                Direction = direction,
                FromWall = c.OldTime, ToWall = c.NewTime,
                Note = note
            };
            db.TimeAnomalies.Add(anomaly);
            await db.SaveChangesAsync();
            if (status == TimeAnomalyStatus.Confirmed) NotifySafe(anomaly);
        }
    }

    /// <summary>
    /// HeartbeatService 的检测回调。不向外抛异常——否则心跳循环会被中止，
    /// 每 30s 产生重复异常行。
    /// </summary>
    public void OnAnomalyDetected(TimeAnomaly anomaly)
    {
        try
        {
            if (anomaly.Status == TimeAnomalyStatus.Confirmed)
            {
                NotifySafe(anomaly);
                return;
            }
            if (anomaly.Status != TimeAnomalyStatus.Suspicious || !_settings.Settings.UseNtp)
                return;
            _ = ConfirmWithNtpAsync(anomaly);
        }
        catch (Exception ex) { _logger.LogError(ex, "OnAnomalyDetected callback failed"); }
    }

    private async Task ConfirmWithNtpAsync(TimeAnomaly anomaly)
    {
        try
        {
            var result = await _ntp.RequestCheckAsync();
            if (result is not { Succeeded: true }) return;
            // RequestCheckAsync also emits ResultArrived. Explicitly await one
            // pass as well; conditional updates keep the two paths idempotent.
            await ReevaluateUnconfirmedWithNtpAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "NTP confirmation failed"); }
    }

    /// <summary>
    /// NTP 结果到达后重评未定/未确认的异常：
    /// Pending+EventLog：当前墙钟 ≈ 参照 → 变化前记录错；墙钟偏差 ≈
    /// 异常偏移 → 变化后记录错。Heartbeat Suspicious/Drift 只在当前偏差
    /// 与检测偏移吻合时确认。其余未确认项在成功校时后转为 Resolved，
    /// 保留审计记录但不再作为活动异常；旧版无 NTP 证据的 Heartbeat
    /// Confirmed 行也会在此收敛，避免升级后永久残留误报。
    /// </summary>
    public async Task ReevaluateUnconfirmedWithNtpAsync()
    {
        try
        {
            var result = _ntp.LastResult;
            if (result is not { Succeeded: true }) return;
            var corroborationFloor = (_ntp.LastQueryAt ?? DateTime.UtcNow).AddMinutes(-2);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.TimeAnomalies.AsNoTracking()
                .Where(a => a.Status == TimeAnomalyStatus.Pending
                    || a.Status == TimeAnomalyStatus.Suspicious
                    || a.Status == TimeAnomalyStatus.Drift
                    || (a.Status == TimeAnomalyStatus.Confirmed
                        && a.Source == TimeAnomalySources.Heartbeat
                        && a.Direction == null
                        && a.LastAppliedAt == null))
                .ToListAsync();
            foreach (var row in rows)
            {
                var previousStatus = row.Status;
                string? direction = null;
                var fresh = row.DetectedAt >= corroborationFloor;
                if (fresh && row.Source == TimeAnomalySources.EventLog
                    && row.Status == TimeAnomalyStatus.Pending
                    && Math.Abs(row.OffsetSeconds) >= _settings.Settings.TimeAnomalyThresholdSeconds)
                {
                    direction = ReferenceDirection(row.OffsetSeconds, result.OffsetSeconds,
                        _settings.Settings.NtpEpsilonSeconds);
                }
                else if (fresh && row.Source == TimeAnomalySources.Heartbeat
                    && Math.Abs(result.OffsetSeconds - row.OffsetSeconds)
                        <= _settings.Settings.NtpEpsilonSeconds)
                {
                    direction = "post";
                }

                var nextStatus = direction == null
                    ? TimeAnomalyStatus.Resolved
                    : TimeAnomalyStatus.Confirmed;
                var note = direction switch
                {
                    "pre" => $"变化前记录错（NTP 复核，{result.SourceName}）",
                    "post" => $"变化后记录错（NTP 确认偏移吻合，{result.SourceName}）",
                    _ when fresh => $"NTP 校时后未证实该异常（{result.SourceName}），已解除",
                    _ => $"旧异常无法由当前 NTP 样本证实（{result.SourceName}），已解除"
                };

                var affected = await db.TimeAnomalies
                    .Where(a => a.Id == row.Id && a.Status == row.Status)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, nextStatus)
                        .SetProperty(a => a.Direction, direction)
                        .SetProperty(a => a.Note, note));
                if (affected == 1)
                {
                    row.Status = nextStatus;
                    row.Direction = direction;
                    row.Note = note;
                    if (nextStatus == TimeAnomalyStatus.Confirmed
                        && previousStatus != TimeAnomalyStatus.Confirmed)
                        NotifySafe(row);
                    else if (nextStatus == TimeAnomalyStatus.Resolved)
                        CancelReminderSafe(row.Id);
                }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Unconfirmed anomaly re-evaluation failed"); }
    }

    /// <summary>
    /// 通知抑制：启动回查/事件日志摄入时，异常的
    /// DetectedAt 早于当前时间 24 小时以上 → 抑制 notifier 调用（仍写行、仍在面板
    /// 可见，只是不弹 toast）——避免长时间停机后启动瞬间为几十天前的每条变更各弹一次。
    /// 所有通知出口统一经 NotifySafe，这里一处守卫覆盖全部。
    /// </summary>
    private bool ShouldNotify(TimeAnomaly anomaly)
        => anomaly.DetectedAt >= DateTime.UtcNow.AddHours(-24);

    private void NotifySafe(TimeAnomaly anomaly)
    {
        if (!ShouldNotify(anomaly)) return;
        try { _notifier?.NotifyConfirmed(anomaly); }
        catch (Exception ex) { _logger.LogError(ex, "Anomaly notifier failed"); }
    }

    private void CancelReminderSafe(long anomalyId)
    {
        try { _notifier?.CancelReminder(anomalyId); }
        catch (Exception ex) { _logger.LogError(ex, "Anomaly reminder cancellation failed"); }
    }

    private static string? ReferenceDirection(double detectedOffset, double referenceOffset, double epsilon)
    {
        if (Math.Abs(referenceOffset) <= epsilon) return "pre";
        if (Math.Abs(referenceOffset - detectedOffset) <= epsilon) return "post";
        return null;
    }

    public List<TimeAnomaly> GetAnomalies(int limit)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.TimeAnomalies.AsNoTracking()
            .OrderByDescending(a => a.DetectedAt).Take(limit).ToList();
    }

    public TimeAnomaly? Get(long id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.TimeAnomalies.AsNoTracking().FirstOrDefault(a => a.Id == id);
    }

    public async Task<TimeReferenceResult?> CheckNtpNow()
    {
        var result = await _ntp.RequestCheckAsync();
        // ResultArrived also schedules this pass, but the Settings page reloads
        // immediately after this endpoint returns. Await an idempotent pass so
        // stale Suspicious/Drift/Pending rows cannot flash as still active.
        if (result is { Succeeded: true })
            await ReevaluateUnconfirmedWithNtpAsync();
        return result;
    }

    /// <summary>
    /// 启动自动应用：优先复用已持久化方向，否则用本次 NTP 结果判定。
    /// 方向不可判定时留给面板。心跳与事件日志对同一次改钟产生重叠异常时，
    /// 只应用一次并将重复项标记为 Ignored。每条独立处理。
    /// </summary>
    public async Task ApplyAllAutoAsync(TimeReferenceResult? ntp = null)
    {
        var ntpResult = ntp;
        var apply = new TimeOffsetApplyService(_scopeFactory);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.TimeAnomalies.AsNoTracking()
            .Where(a => (a.Status == TimeAnomalyStatus.Confirmed
                    && a.Source == TimeAnomalySources.Heartbeat
                    && Math.Abs(a.OffsetSeconds) >= _settings.Settings.TimeAnomalyThresholdSeconds)
                || (a.Status == TimeAnomalyStatus.Applied && a.Direction != null))
            .ToListAsync();

        var applied = new List<(TimeAnomaly Anomaly, PlanInfo Plan)>();
        foreach (var a in rows.Where(a => a.Status == TimeAnomalyStatus.Applied))
        {
            var plan = await apply.ResolveAsync(a.Id, null, ntp: null);
            if (plan != null && plan.Shift != 0) applied.Add((a, plan));
        }

        foreach (var a in rows.Where(a => a.Status == TimeAnomalyStatus.Confirmed
                         && a.Source == TimeAnomalySources.Heartbeat)
                     .OrderBy(a => a.DetectedAt))
        {
            try
            {
                var plan = await apply.ResolveAsync(a.Id, null, ntpResult);
                if (plan == null || plan.Shift == 0) continue;
                var duplicate = applied.FirstOrDefault(x => IsDuplicate(a, plan, x.Anomaly, x.Plan));
                if (duplicate.Anomaly != null)
                {
                    await db.TimeAnomalies
                        .Where(x => x.Id == a.Id && x.Status == TimeAnomalyStatus.Confirmed)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Status, TimeAnomalyStatus.Ignored)
                            .SetProperty(x => x.Direction, plan.Direction)
                            .SetProperty(x => x.Note,
                                $"与异常 #{duplicate.Anomaly.Id} 为同一次时钟变化，未重复应用"));
                    _logger.LogInformation(
                        "Auto-apply ignored duplicate anomaly {Id}; original anomaly {OriginalId}",
                        a.Id, duplicate.Anomaly.Id);
                    continue;
                }
                var r = await apply.Apply(a.Id, null, ntpResult);
                if (r != null) applied.Add((a, plan));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Auto-apply failed for anomaly {Id}", a.Id); }
        }
    }

    private static bool IsDuplicate(TimeAnomaly xAnomaly, PlanInfo x,
        TimeAnomaly yAnomaly, PlanInfo y) =>
        !string.Equals(xAnomaly.Source, yAnomaly.Source, StringComparison.Ordinal)
        && Math.Abs((xAnomaly.DetectedAt - yAnomaly.DetectedAt).TotalMinutes) <= 2
        && Math.Abs(x.Shift - y.Shift) < 0.001
        && (x.From ?? DateTime.MinValue) < (y.To ?? DateTime.MaxValue)
        && (y.From ?? DateTime.MinValue) < (x.To ?? DateTime.MaxValue);

    /// <summary>
    /// Event-log backfill starts from the newest durable cursor with a one-day
    /// rollback allowance. This keeps startup work bounded as the activity DB
    /// ages while duplicate detection makes the overlap safe.
    /// </summary>
    public static async Task<DateTime> GetLastKnownTimeAsync(AppDbContext db)
    {
        var hb = await db.Heartbeats.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
        var lastEventLog = await db.TimeAnomalies.AsNoTracking()
            .Where(a => a.Source == TimeAnomalySources.EventLog)
            .MaxAsync(a => (DateTime?)a.DetectedAt);
        var cursor = new[]
            {
                hb != null && hb.LastTick != default ? hb.LastTick : (DateTime?)null,
                lastEventLog
            }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty(DateTime.UtcNow)
            .Max();
        return cursor.AddDays(-1);
    }

    public override void Dispose()
    {
        _ntp.ResultArrived -= OnNtpResultArrived;
        _watcher?.Dispose();
        base.Dispose();
    }
}
