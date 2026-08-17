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
/// 的检测回调（可疑 → NTP 确认）；Confirmed 通知；应用/恢复在 Task 8 加入。
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
            // 先取一次 NTP 结果再回查事件日志：否则启动时 LastResult 必为 null，
            // 所有事件日志异常会直接标 Pending（与规格"NTP 可用自动定方向"不符）。
            // UseNtp=false 时 RequestCheckAsync 立即返回 null，无需等待。
            if (_settings.Settings.UseNtp)
                await _ntp.RequestCheckAsync();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = await GetLastKnownTimeAsync(db);
            var changes = _logReader.GetChangesSince(since);
            await IngestEventLogChanges(changes, _ntp.LastResult, db);

            // 后续 NTP 结果到达（周期调度/按需补查）时，重评启动期标 Pending 的事件日志异常。
            _ntp.ResultArrived += OnNtpResultArrived;
            _watcher = _logReader.StartWatching(c => _ = IngestSafeAsync(new[] { c }, _ntp.LastResult));
        }
        catch (Exception ex) { _logger.LogError(ex, "Startup event-log back-query failed"); }

        // 启动自动应用：方向可自动判定的 Confirmed 异常（Heartbeat + EventLog/NTP 已定方向）
        // 立即修复数据；单条失败不中断（ApplyAllAutoAsync 内部逐条 try/catch）。
        try { await ApplyAllAutoAsync(); }
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

    public async Task IngestEventLogChanges(IReadOnlyList<SystemTimeChange> changes,
        TimeReferenceResult? ntp, AppDbContext db)
    {
        foreach (var c in changes)
        {
            // 防重复摄入：重启回查（GetChangesSince(GetLastKnownTimeAsync)）会与上一会话
            // watcher 重叠摄入 [lastHeartbeat, shutdown] 窗口内的事件，同一 Kernel-General
            // 事件被摄入两次 → TimeAnomalies 重复行 → ApplyAllAutoAsync 对同一段应用两次
            // 偏移 → 静默数据损坏。同源同 Old/NewTime 已存在则跳过（事件日志行的
            // OldTime/NewTime 恒非空，ParseEventXml 要求，普通相等即可）。
            var already = await db.TimeAnomalies.AnyAsync(a =>
                a.Source == TimeAnomalySources.EventLog
                && a.OldTime == c.OldTime
                && a.NewTime == c.NewTime);
            if (already) continue;

            var delta = (c.NewTime - c.OldTime).TotalSeconds;
            string status; string? note;
            if (ntp is { Succeeded: true })
            {
                // 当前墙钟 ≈ 参照 → 变化是修正，旧记录错；否则新记录错。
                // 段方向在应用时决定；此处 NTP 可用即视为确认异常。
                status = TimeAnomalyStatus.Confirmed;
                note = Math.Abs(ntp.OffsetSeconds) <= _settings.Settings.NtpEpsilonSeconds
                    ? "变化前记录错（变化为修正）"
                    : "变化后记录错（变化为改动）";
            }
            else
            {
                status = TimeAnomalyStatus.Pending;
                note = "无 NTP 参照，需选择修复方向";
            }
            var anomaly = new TimeAnomaly
            {
                // Ruling B2：DetectedAt 取变化发生时刻（而非摄入时刻）——否则启动回查
                // 给几十天前的每条变更都打"刚刚检测到"，DetectedAt 过旧抑制无法生效，
                // 面板排序/清理也会受启动瞬间的批量摄入影响。
                DetectedAt = c.OccurredAt.ToUniversalTime(),
                Source = TimeAnomalySources.EventLog,
                OldTime = c.OldTime, NewTime = c.NewTime,
                OffsetSeconds = delta,
                Status = status,
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
            // 墙钟当前偏差 ≈ 检测偏移 → 确认为异常
            if (Math.Abs(result.OffsetSeconds - anomaly.OffsetSeconds) <= _settings.Settings.NtpEpsilonSeconds)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var note = $"NTP 确认（{result.SourceName}）";
                var affected = await db.TimeAnomalies
                    .Where(a => a.Id == anomaly.Id && a.Status == TimeAnomalyStatus.Suspicious)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, TimeAnomalyStatus.Confirmed)
                        .SetProperty(a => a.Note, note));
                // 条件原子更新：与 ResultArrived→ReevaluateUnconfirmedWithNtpAsync 并发时
                // 只有一次 affected>0，防止同一异常双重通知。
                if (affected > 0)
                {
                    anomaly.Status = TimeAnomalyStatus.Confirmed;
                    anomaly.Note = note;
                    NotifySafe(anomaly);
                }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "NTP confirmation failed"); }
    }

    /// <summary>
    /// NTP 结果到达后重评未定/未确认的异常：
    /// Pending+EventLog（启动回查）：当前墙钟 ≈ 参照 → 变化前记录错（修正）；
    /// 墙钟偏差 ≈ 异常偏移 → 变化后记录错；两者都不满足保持 Pending。
    /// Suspicious（任意来源）：墙钟偏差 ≈ 异常偏移 → NTP 确认升级 Confirmed 并通知；
    /// 不满足保持原状。
    /// </summary>
    public async Task ReevaluateUnconfirmedWithNtpAsync()
    {
        try
        {
            var result = _ntp.LastResult;
            if (result is not { Succeeded: true }) return;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.TimeAnomalies.AsNoTracking()
                .Where(a => (a.Status == TimeAnomalyStatus.Pending
                        && a.Source == TimeAnomalySources.EventLog)
                    || a.Status == TimeAnomalyStatus.Suspicious)
                .ToListAsync();
            foreach (var row in rows)
            {
                string? note;
                if (row.Status == TimeAnomalyStatus.Pending)
                {
                    if (Math.Abs(result.OffsetSeconds) <= _settings.Settings.NtpEpsilonSeconds)
                        note = $"变化前记录错（NTP 复核，{result.SourceName}）";
                    else if (Math.Abs(result.OffsetSeconds - row.OffsetSeconds) <= _settings.Settings.NtpEpsilonSeconds)
                        note = $"变化后记录错（NTP 复核，{result.SourceName}）";
                    else
                        continue;
                }
                else // Suspicious 行：NTP 偏差与检测偏移吻合 → 确认异常
                {
                    if (Math.Abs(result.OffsetSeconds - row.OffsetSeconds) > _settings.Settings.NtpEpsilonSeconds)
                        continue;
                    note = $"NTP 确认（{result.SourceName}）";
                }
                // 条件原子更新：仅当该行仍处于原状态时才升级；与 ConfirmWithNtpAsync
                // 并发时只有一次 affected==1，防止同一异常双重通知。
                var affected = await db.TimeAnomalies
                    .Where(a => a.Id == row.Id && a.Status == row.Status)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, TimeAnomalyStatus.Confirmed)
                        .SetProperty(a => a.Note, note));
                if (affected == 1)
                {
                    row.Status = TimeAnomalyStatus.Confirmed;
                    row.Note = note;
                    NotifySafe(row);
                }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Unconfirmed anomaly re-evaluation failed"); }
    }

    /// <summary>
    /// 通知抑制（Ruling B2，Task 7 Minor #2）：启动回查/事件日志摄入时，异常的
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

    public async Task<TimeReferenceResult?> CheckNtpNow() => await _ntp.RequestCheckAsync();

    /// <summary>
    /// 启动自动应用：只处理方向可自动判定的异常——
    /// Heartbeat 来源 Confirmed（方向由偏移符号决定，direction null）；
    /// EventLog 来源 Confirmed 按 Note 前缀定方向（「变化前」→ "pre" /「变化后」→ "post"）；
    /// Pending 跳过（等待面板方向选择）。每条独立 try/catch，单条失败不中断其余。
    /// </summary>
    public async Task ApplyAllAutoAsync()
    {
        var apply = new TimeOffsetApplyService(_scopeFactory);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var heartbeats = await db.TimeAnomalies.AsNoTracking()
            .Where(a => a.Status == TimeAnomalyStatus.Confirmed
                && a.Source == TimeAnomalySources.Heartbeat)
            .ToListAsync();
        foreach (var a in heartbeats)
        {
            try { await apply.Apply(a.Id, null); }
            catch (Exception ex) { _logger.LogWarning(ex, "Auto-apply failed for anomaly {Id}", a.Id); }
        }

        var eventLogs = await db.TimeAnomalies.AsNoTracking()
            .Where(a => a.Status == TimeAnomalyStatus.Confirmed
                && a.Source == TimeAnomalySources.EventLog)
            .ToListAsync();
        foreach (var a in eventLogs)
        {
            try
            {
                var direction = a.Note?.Contains("变化前") == true ? "pre"
                    : a.Note?.Contains("变化后") == true ? "post"
                    : null;
                if (direction != null)
                    await apply.Apply(a.Id, direction);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Auto-apply failed for anomaly {Id}", a.Id); }
        }
    }

    private static async Task<DateTime> GetLastKnownTimeAsync(AppDbContext db)
    {
        var hb = await db.Heartbeats.FindAsync(1);
        if (hb != null && hb.LastTick != default) return hb.LastTick;
        var last = await db.SystemEvents.OrderByDescending(e => e.Timestamp).FirstOrDefaultAsync();
        return last?.Timestamp ?? DateTime.UtcNow.AddDays(-30);
    }

    public override void Dispose()
    {
        _ntp.ResultArrived -= OnNtpResultArrived;
        _watcher?.Dispose();
        base.Dispose();
    }
}
