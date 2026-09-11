// 偏移应用/恢复引擎。
//
// 四种方向场景（段 → 应用方向规则）：
//   HeartbeatForward  前调（OffsetSeconds > 0）：shift = −OffsetSeconds，
//                     范围 [FromWall, +∞)，上界封顶 now + |offset|；
//                     空洞保证（正常记录全部 < FromWall、污染记录全部 ≥ FromWall），
//                     前调无回溯修改。
//   HeartbeatBackward 回调（OffsetSeconds < 0）：shift = |OffsetSeconds|，
//                     范围 [FromWall − |offset|, FromWall]，best-effort
//                     （重叠区无法区分新旧记录，规格接受）。
//   EventLogPre       变化前记录错（变化为修正）：shift = OffsetSeconds（= δ 修正量），
//                     范围 (−∞, min(OldTime, NewTime)]——只平移无歧义的旧区；
//                     (NewTime, OldTime] 重叠区变化前后均可能落字，跳过不碰。
//   EventLogPost      变化后记录错（变化为改动）：shift = −OffsetSeconds（= −δ），
//                     范围 [max(OldTime, NewTime), +∞)——只平移无歧义的新区。
//
// 平移覆盖（每张表的范围谓词与平移列，五表全覆盖）：
//   FocusChanges.Timestamp
//   WindowSessions.OpenTime / CloseTime（CloseTime 非空且落范围才平移）
//   ProcessSessions.StartTime / EndTime（EndTime 同上）
//   MediaSessionRecords.StartTime / EndTime（EndTime 同上）
//   SystemEvents.Timestamp
//
// 流水：按列 SELECT 受影响 Id（含每列 MaxRowTimestamp）→ 压缩为连续 Id 区间 →
// 写 TimeOffsetApplication（RowsJson + ShiftSeconds + MaxRowTimestamp，先于 UPDATE
// 落库）→ 按主键区间 ExecuteUpdateAsync → 异常标 Applied（LastAppliedAt）。
// 恢复：按 RowsJson 的列级 Id 区间 −ShiftSeconds 反向平移 → RevertedAt 置位 →
// 异常标 Reverted。双列表按列分开记录区间，恢复是 apply 的严格逆变换
// （表级 Id 列表无法区分"仅 CloseTime 被平移"的行，故按列记录）。
// scoped DbContext 直用，不用 WriteQueue——批量 UPDATE 不走队列。
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Core.Services;

public sealed class ApplyPreview { public Dictionary<string, int> TableCounts { get; set; } = new(); }
public sealed class ApplyResult
{
    public long AnomalyId { get; set; }
    public double ShiftSeconds { get; set; }
    public Dictionary<string, int> TableCounts { get; set; } = new();
    public int AppliedRowsTotal { get; set; }
}
public sealed class RestoreResult { public int RestoredRowsTotal { get; set; } }

/// <summary>方向解析结果（供自动应用去重与预览）：方向 + 平移范围 + 平移量。</summary>
public sealed record PlanInfo(string Direction, DateTime? From, DateTime? To, double Shift);

public class TimeOffsetApplyService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly SemaphoreSlim WalCheckpointGate = new(1, 1);

    public TimeOffsetApplyService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    private enum Scenario { HeartbeatForward, HeartbeatBackward, EventLogPre, EventLogPost }

    /// <summary>一次应用的方向判定结果：平移量 + 范围（含边界是否有界）。</summary>
    private sealed record ShiftPlan(Scenario Scenario, double Shift,
        DateTime? From, DateTime? To, bool FromBounded, bool ToBounded);

    /// <summary>每列的范围谓词 + 平移表达式。Key = 日志键（单列表 = 表名；双列表 = "表.列"）。</summary>
    private sealed record ColumnPlan(
        string Key, string Table,
        Func<AppDbContext, ShiftPlan, Task<(List<long> Ids, DateTime? MaxTs)>> CollectAsync,
        Func<AppDbContext, IReadOnlyList<JournalIdRange>, double, Task> ShiftAsync);

    private static readonly ColumnPlan[] Plans =
    {
        // ---- FocusChanges.Timestamp ----
        new("FocusChanges", "FocusChanges",
            async (db, p) =>
            {
                IQueryable<FocusChange> q = db.FocusChanges.AsNoTracking();
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(f => f.Timestamp >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(f => f.Timestamp <= tv);
                }
                q = q.OrderBy(f => f.Id);
                return (await q.Select(f => f.Id).ToListAsync(), await q.MaxAsync(f => (DateTime?)f.Timestamp));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.FocusChanges.Where(f => f.Id >= range.First && f.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(f => f.Timestamp, f => f.Timestamp.AddSeconds(shift)));
            }),

        // ---- WindowSessions.OpenTime ----
        new("WindowSessions.OpenTime", "WindowSessions",
            async (db, p) =>
            {
                IQueryable<WindowSession> q = db.WindowSessions.AsNoTracking();
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(w => w.OpenTime >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(w => w.OpenTime <= tv);
                }
                q = q.OrderBy(w => w.Id);
                return (await q.Select(w => w.Id).ToListAsync(), await q.MaxAsync(w => (DateTime?)w.OpenTime));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.WindowSessions.Where(w => w.Id >= range.First && w.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(w => w.OpenTime, w => w.OpenTime.AddSeconds(shift)));
            }),

        // ---- WindowSessions.CloseTime（非空且落范围才平移） ----
        new("WindowSessions.CloseTime", "WindowSessions",
            async (db, p) =>
            {
                IQueryable<WindowSession> q = db.WindowSessions.AsNoTracking().Where(w => w.CloseTime != null);
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(w => w.CloseTime >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(w => w.CloseTime <= tv);
                }
                q = q.OrderBy(w => w.Id);
                return (await q.Select(w => w.Id).ToListAsync(), await q.MaxAsync(w => (DateTime?)w.CloseTime));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.WindowSessions.Where(w => w.Id >= range.First && w.Id <= range.Last && w.CloseTime != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(w => w.CloseTime, w => w.CloseTime!.Value.AddSeconds(shift)));
            }),

        // ---- ProcessSessions.StartTime ----
        new("ProcessSessions.StartTime", "ProcessSessions",
            async (db, p) =>
            {
                IQueryable<ProcessSession> q = db.ProcessSessions.AsNoTracking();
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(x => x.StartTime >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(x => x.StartTime <= tv);
                }
                q = q.OrderBy(x => x.Id);
                return (await q.Select(x => x.Id).ToListAsync(), await q.MaxAsync(x => (DateTime?)x.StartTime));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.ProcessSessions.Where(x => x.Id >= range.First && x.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartTime, x => x.StartTime.AddSeconds(shift)));
            }),

        // ---- ProcessSessions.EndTime（非空且落范围才平移） ----
        new("ProcessSessions.EndTime", "ProcessSessions",
            async (db, p) =>
            {
                IQueryable<ProcessSession> q = db.ProcessSessions.AsNoTracking().Where(x => x.EndTime != null);
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(x => x.EndTime >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(x => x.EndTime <= tv);
                }
                q = q.OrderBy(x => x.Id);
                return (await q.Select(x => x.Id).ToListAsync(), await q.MaxAsync(x => (DateTime?)x.EndTime));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.ProcessSessions.Where(x => x.Id >= range.First && x.Id <= range.Last && x.EndTime != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndTime, x => x.EndTime!.Value.AddSeconds(shift)));
            }),

        // ---- MediaSessionRecords.StartTime ----
        new("MediaSessionRecords.StartTime", "MediaSessionRecords",
            async (db, p) =>
            {
                IQueryable<MediaSessionRecord> q = db.MediaSessionRecords.AsNoTracking();
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(m => m.StartTime >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(m => m.StartTime <= tv);
                }
                q = q.OrderBy(m => m.Id);
                return (await q.Select(m => m.Id).ToListAsync(), await q.MaxAsync(m => (DateTime?)m.StartTime));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.MediaSessionRecords.Where(m => m.Id >= range.First && m.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, m => m.StartTime.AddSeconds(shift)));
            }),

        // ---- MediaSessionRecords.EndTime（非空且落范围才平移） ----
        new("MediaSessionRecords.EndTime", "MediaSessionRecords",
            async (db, p) =>
            {
                IQueryable<MediaSessionRecord> q = db.MediaSessionRecords.AsNoTracking().Where(m => m.EndTime != null);
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(m => m.EndTime >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(m => m.EndTime <= tv);
                }
                q = q.OrderBy(m => m.Id);
                return (await q.Select(m => m.Id).ToListAsync(), await q.MaxAsync(m => (DateTime?)m.EndTime));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.MediaSessionRecords.Where(m => m.Id >= range.First && m.Id <= range.Last && m.EndTime != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.EndTime, m => m.EndTime!.Value.AddSeconds(shift)));
            }),

        // ---- SystemEvents.Timestamp ----
        new("SystemEvents", "SystemEvents",
            async (db, p) =>
            {
                IQueryable<SystemEvent> q = db.SystemEvents.AsNoTracking();
                if (p.FromBounded)  // From null → 无下界
                {
                    var fv = p.From!.Value;
                    q = q.Where(e => e.Timestamp >= fv);
                }
                if (p.ToBounded)    // To null → 无上界
                {
                    var tv = p.To!.Value;
                    q = q.Where(e => e.Timestamp <= tv);
                }
                q = q.OrderBy(e => e.Id);
                return (await q.Select(e => e.Id).ToListAsync(), await q.MaxAsync(e => (DateTime?)e.Timestamp));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.SystemEvents.Where(e => e.Id >= range.First && e.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.Timestamp, e => e.Timestamp.AddSeconds(shift)));
            })
    };

    private static ColumnPlan? FindPlan(string key)
        => Array.Find(Plans, x => x.Key == key);

    /// <summary>
    /// 方向判定（Phase B）：显式 direction（面板选择）优先；否则用新鲜 NTP 结果判定——
    /// 不再解析 Note 文案（旧实现把方向烘焙进中文 Note，NTP 过期时方向错误并静默损坏数据）。
    /// 判定不出方向或偏移为零返回 null。
    /// </summary>
    private static ShiftPlan? ResolvePlan(TimeAnomaly anomaly, string? direction,
        TimeReferenceResult? ntp, double epsilonSec, DateTime now)
    {
        if (!string.IsNullOrEmpty(direction))
            return ResolveByDirection(anomaly, direction, now);

        if (!string.IsNullOrEmpty(anomaly.Direction))
            return ResolveByDirection(anomaly, anomaly.Direction, now);

        if (anomaly.Source == TimeAnomalySources.Heartbeat)
            return ResolveHeartbeatWithNtp(anomaly, ntp, epsilonSec, now);

        if (anomaly.Source == TimeAnomalySources.EventLog)
            return ResolveEventLogWithNtp(anomaly, ntp, epsilonSec);

        return null;
    }

    /// <summary>显式方向：pre = 旧段错（平移 +offset）；post = 新段错（平移 −offset）。</summary>
    private static ShiftPlan? ResolveByDirection(TimeAnomaly anomaly, string direction, DateTime now)
    {
        var offset = anomaly.OffsetSeconds;
        if (offset == 0) return null;
        var isPre = string.Equals(direction, "pre", StringComparison.OrdinalIgnoreCase);
        var isPost = string.Equals(direction, "post", StringComparison.OrdinalIgnoreCase);
        if (!isPre && !isPost) return null;

        if (anomaly.Source == TimeAnomalySources.Heartbeat)
        {
            var boundary = anomaly.FromWall ?? anomaly.DetectedAt;
            if (offset > 0)
            {
                // 前调：新段 = [FromWall, +∞)；旧段 = (−∞, FromWall]。
                if (isPre) return new ShiftPlan(Scenario.EventLogPre, offset, null, boundary, false, true);
                return new ShiftPlan(Scenario.HeartbeatForward, -offset, boundary,
                    anomaly.ToWall ?? now.AddSeconds(offset), true, true);
            }
            // 回调：新段（重叠区）= [FromWall−|δ|, FromWall]；旧段 = (−∞, FromWall−|δ|]。
            var from = boundary.AddSeconds(offset);
            if (isPre) return new ShiftPlan(Scenario.EventLogPre, offset, null, from, false, true);
            return new ShiftPlan(Scenario.HeartbeatBackward, -offset, from, boundary, true, true);
        }

        // EventLog：pre 平移 (−∞, min(Old,New)]；post 平移 [max(Old,New), +∞)。
        var old = anomaly.OldTime ?? anomaly.FromWall;
        var @new = anomaly.NewTime ?? anomaly.ToWall;
        if (isPre)
        {
            var to = old ?? @new ?? anomaly.DetectedAt;
            if (@new != null && @new.Value < to) to = @new.Value;
            return new ShiftPlan(Scenario.EventLogPre, offset, null, to, false, true);
        }
        var fromE = @new ?? old ?? anomaly.DetectedAt;
        if (old != null && old.Value > fromE) fromE = old.Value;
        return new ShiftPlan(Scenario.EventLogPost, -offset, fromE, null, true, false);
    }

    /// <summary>
    /// 心跳来源：新鲜 NTP 判定"修正"还是"篡改"。判定不出（无 NTP 或当前时钟态与
    /// 检测偏移不符）返回 null，留给面板。
    /// 修正（当前时钟正确，|ntp|≤ε）：跳变是系统/用户把错误时钟纠正 → 变化前记录错 → 平移旧段 +offset。
    /// 篡改（当前时钟仍偏检测偏移，|ntp−offset|≤ε）：跳变是改时钟 → 变化后记录错 → 平移新段 −offset。
    /// </summary>
    private static ShiftPlan? ResolveHeartbeatWithNtp(TimeAnomaly anomaly,
        TimeReferenceResult? ntp, double epsilonSec, DateTime now)
    {
        var offset = anomaly.OffsetSeconds;
        if (offset == 0) return null;
        if (ntp is not { Succeeded: true }) return null;

        if (Math.Abs(ntp.OffsetSeconds) <= epsilonSec)
        {
            // 修正：平移旧段 (+offset)。前调旧段 = (−∞, FromWall]；回调旧段 = (−∞, FromWall−|δ|]。
            var to = (anomaly.FromWall ?? anomaly.DetectedAt).AddSeconds(Math.Min(0, offset));
            return new ShiftPlan(Scenario.EventLogPre, offset, null, to, false, true);
        }
        if (Math.Abs(ntp.OffsetSeconds - offset) <= epsilonSec)
        {
            if (offset > 0)
            {
                // 前调篡改：新段 [FromWall, +∞) 平移 −offset。
                var from = anomaly.FromWall ?? anomaly.DetectedAt;
                return new ShiftPlan(Scenario.HeartbeatForward, -offset, from,
                    anomaly.ToWall ?? now.AddSeconds(offset), true, true);
            }
            // 回调篡改：新段（重叠区）[FromWall−|δ|, FromWall] 平移 −offset（= +|δ|）。
            var to = anomaly.FromWall ?? anomaly.DetectedAt;
            return new ShiftPlan(Scenario.HeartbeatBackward, -offset, to.AddSeconds(offset), to, true, true);
        }
        return null;
    }

    /// <summary>
    /// 事件日志来源：新鲜 NTP 判定方向。当前时钟正确 → 变化是修正 → 旧段错（pre）；
    /// 当前时钟仍偏检测偏移 → 变化是篡改 → 新段错（post）。无新鲜 NTP 不判定。
    /// </summary>
    private static ShiftPlan? ResolveEventLogWithNtp(TimeAnomaly anomaly,
        TimeReferenceResult? ntp, double epsilonSec)
    {
        var old = anomaly.OldTime ?? anomaly.FromWall;
        var @new = anomaly.NewTime ?? anomaly.ToWall;
        if (ntp is not { Succeeded: true }) return null;

        if (Math.Abs(ntp.OffsetSeconds) <= epsilonSec)
        {
            var to = old ?? @new ?? anomaly.DetectedAt;
            if (@new != null && @new.Value < to) to = @new.Value;
            return new ShiftPlan(Scenario.EventLogPre, anomaly.OffsetSeconds, null, to,
                FromBounded: false, ToBounded: true);
        }
        if (Math.Abs(ntp.OffsetSeconds - anomaly.OffsetSeconds) <= epsilonSec)
        {
            var from = @new ?? old ?? anomaly.DetectedAt;
            if (old != null && old.Value > from) from = old.Value;
            return new ShiftPlan(Scenario.EventLogPost, -anomaly.OffsetSeconds, from, null,
                FromBounded: true, ToBounded: false);
        }
        return null;
    }

    private static string ScenarioDirection(ShiftPlan plan)
        => plan.Scenario == Scenario.EventLogPre ? "pre" : "post";

    /// <summary>解析异常的方向计划（不写库）。供自动应用的去重与预览共用。</summary>
    public async Task<PlanInfo?> ResolveAsync(long anomalyId, string? direction, TimeReferenceResult? ntp)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var anomaly = await db.TimeAnomalies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == anomalyId);
        if (anomaly == null) return null;
        var plan = ResolvePlan(anomaly, direction, ntp, settings.Settings.NtpEpsilonSeconds, DateTime.UtcNow);
        return plan == null ? null
            : new PlanInfo(ScenarioDirection(plan), plan.From, plan.To, plan.Shift);
    }

    /// <summary>预览：每表受影响行数（direction 可空；ntp 为新鲜参照，方向自动判定用）。</summary>
    public ApplyPreview? Preview(long anomalyId, string? direction = null, TimeReferenceResult? ntp = null)
        => PreviewAsync(anomalyId, direction, ntp).GetAwaiter().GetResult();

    public async Task<ApplyPreview?> PreviewAsync(long anomalyId, string? direction = null,
        TimeReferenceResult? ntp = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var anomaly = await db.TimeAnomalies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == anomalyId);
        if (anomaly == null) return null;
        var plan = ResolvePlan(anomaly, direction, ntp, settings.Settings.NtpEpsilonSeconds, DateTime.UtcNow);
        if (plan == null) return null;

        var perTable = new Dictionary<string, HashSet<long>>();
        foreach (var p in Plans)
        {
            var (ids, _) = await p.CollectAsync(db, plan);
            if (ids.Count == 0) continue;
            if (!perTable.TryGetValue(p.Table, out var set)) perTable[p.Table] = set = new HashSet<long>();
            set.UnionWith(ids);
        }
        return new ApplyPreview
        {
            TableCounts = perTable.ToDictionary(kv => kv.Key, kv => kv.Value.Count)
        };
    }

    /// <summary>
    /// 应用偏移。direction 为显式方向（面板选择，任意来源可用）；ntp 为新鲜参照，
    /// direction 缺省时用它判定方向。状态必须是 Confirmed/Pending/Reverted
    /// （Applied 拒绝重复应用）。
    /// 返回 null 表示：异常不存在 / 状态不允许 / 方向不可判定 / 无可平移行。
    ///
    /// 并发控制：事务内第一步做守卫 UPDATE（先取写锁 + 状态条件）。
    /// Microsoft.Data.Sqlite 默认 busy_timeout 30s——竞争方通常等待而非抛错，若先采集
    /// 行再写会基于陈旧行 ID 快照继续 → 双重平移。守卫 UPDATE 让竞争方先拿写锁：
    /// 等到胜者提交后再执行，因状态已非 Confirmed/Pending 而 affected==0，
    /// 直接回滚返回 null，不再采集/平移/写 journal；状态读取与行采集移入守卫之后，
    /// 保证基于提交后状态的一致快照。
    /// </summary>
    public async Task<ApplyResult?> Apply(long anomalyId, string? direction = null,
        TimeReferenceResult? ntp = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            $"PRAGMA journal_size_limit={SqliteMaintenance.JournalSizeLimitBytes}");

        // 守卫声明（事务首个写操作，先拿写锁再采集）：
        // 条件 UPDATE 只在 Status ∈ {Confirmed,Pending,Reverted} 时置 Applied；
        // affected==0 → 并发方已先提交（状态已 Applied）/ 异常不存在 / 状态不允许，
        // 直接回滚返回 null，不产生任何采集、平移、journal。
        var affected = await db.TimeAnomalies
            .Where(a => a.Id == anomalyId &&
                (a.Status == TimeAnomalyStatus.Confirmed
                 || a.Status == TimeAnomalyStatus.Pending
                 || a.Status == TimeAnomalyStatus.Reverted))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, TimeAnomalyStatus.Applied));
        if (affected == 0)
        {
            await tx.RollbackAsync();
            return null;
        }

        // 状态读取与行采集在守卫之后（同事务一致快照）
        var anomaly = await db.TimeAnomalies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == anomalyId);
        if (anomaly == null) { await tx.RollbackAsync(); return null; }
        var plan = ResolvePlan(anomaly, direction, ntp, settings.Settings.NtpEpsilonSeconds, DateTime.UtcNow);
        if (plan == null || plan.Shift == 0) { await tx.RollbackAsync(); return null; }

        // 持久化解析出的方向（面板/日志可回溯），与守卫同事务
        await db.TimeAnomalies
            .Where(a => a.Id == anomalyId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Direction, ScenarioDirection(plan)));

        // 1) 每表每列收集受影响 Id + 列内最大时间戳（ORDER BY Id 保证日志列表确定性）
        var rowsJson = new Dictionary<string, List<long>>();
        var perTable = new Dictionary<string, HashSet<long>>();
        var maxTs = plan.From;
        foreach (var p in Plans)
        {
            var (ids, colMax) = await p.CollectAsync(db, plan);
            if (ids.Count == 0) continue;
            rowsJson[p.Key] = ids;
            if (!perTable.TryGetValue(p.Table, out var set)) perTable[p.Table] = set = new HashSet<long>();
            set.UnionWith(ids);
            if (colMax != null && (maxTs == null || colMax > maxTs)) maxTs = colMax;
        }

        // 当前无可平移行时保留原状态，后续启动可重试尚未写入的数据。
        if (rowsJson.Count == 0)
        {
            await tx.RollbackAsync();
            return null;
        }

        // 连续主键压缩通常把数 MB 的 JSON 数组缩至数 KB，同时后续每个连续
        // 区间只需一条 UPDATE，不再生成数百条 500-id IN (...) 语句。
        var compactRows = rowsJson.ToDictionary(
            pair => pair.Key,
            pair => TimeOffsetJournalCodec.CompressIds(pair.Value),
            StringComparer.Ordinal);

        // 2) 日志先落库：UPDATE 失败时恢复凭据已在（同一事务，异常即回滚）
        var journal = new TimeOffsetApplication
        {
            AnomalyId = anomaly.Id,
            AppliedAt = DateTime.UtcNow,
            ShiftSeconds = plan.Shift,
            RowsJson = TimeOffsetJournalCodec.Encode(compactRows),
            // 平移后最大时间戳——正偏移修复（回调 / EventLog pre δ>0）后，
            // 引用行的新时间戳 ≥ 该值；清理协同按 < cutoff 删除，避免误删仍需恢复凭据的日志。
            MaxRowTimestamp = (maxTs ?? DateTime.UtcNow).AddSeconds(plan.Shift)
        };
        db.TimeOffsetApplications.Add(journal);
        await db.SaveChangesAsync();

        // 3) 按连续 Id 区间平移——WHERE 只按日志区间圈定，仍是精确逆变换。
        foreach (var p in Plans)
            if (compactRows.TryGetValue(p.Key, out var ranges))
                await p.ShiftAsync(db, ranges, plan.Shift);

        // 状态已由守卫置 Applied，这里只补 LastAppliedAt。
        await db.TimeAnomalies
            .Where(a => a.Id == anomalyId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastAppliedAt, DateTime.UtcNow));

        await tx.CommitAsync();
        await tx.DisposeAsync();

        // Large repair transactions can leave a multi-gigabyte physical WAL even
        // after all frames are checkpointed. Truncate opportunistically; an active
        // dashboard reader may make this fail, which is harmless because a later
        // maintenance/startup checkpoint retries it.
        var walTruncated = false;
        try { walTruncated = await SqliteMaintenance.TryTruncateWalAsync(db); }
        catch { }
        if (!walTruncated) ScheduleWalTruncate();

        var tableCounts = perTable.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        return new ApplyResult
        {
            AnomalyId = anomaly.Id,
            ShiftSeconds = plan.Shift,
            TableCounts = tableCounts,
            AppliedRowsTotal = tableCounts.Values.Sum()
        };
    }

    /// <summary>
    /// 恢复：按 RowsJson 的列级 Id 区间 −ShiftSeconds 反向平移（严格逆变换），
    /// RevertedAt 置位，异常标 Reverted。未知键（未来新增表）跳过不阻断。
    /// 整体包事务：RevertedAt 检查与反向平移同事务，闭合双重恢复竞态——
    /// SQLite 写锁串行化并发 Restore，后者写冲突抛异常，不会静默二次平移。
    ///
    /// 并发控制与 Apply 相同：事务内第一步做写优先守卫 UPDATE
    /// （RevertedAt IS NULL → 置当前时间）。SQLite 默认 busy_timeout 30s——竞争方通常
    /// 等待而非抛错，若先读 journal 再写会基于陈旧快照继续 → 双重反向平移。守卫让
    /// 竞争方先拿写锁：等到胜者提交后执行，因 RevertedAt 已非 NULL 而 affected==0，
    /// 直接回滚返回 null，不再读行/反向平移；journal 读取移入守卫之后，保证一致快照。
    /// </summary>
    public async Task<RestoreResult?> Restore(long applicationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();

        // 守卫声明（事务首个写操作，先拿写锁再读）：
        // 条件 UPDATE 只在 RevertedAt IS NULL 时置当前时间；
        // affected==0 → 应用不存在 / 并发方已先恢复，直接回滚返回 null。
        var affected = await db.TimeOffsetApplications
            .Where(x => x.Id == applicationId && x.RevertedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevertedAt, DateTime.UtcNow));
        if (affected == 0)
        {
            await tx.RollbackAsync();
            return null;
        }

        // journal 读取在守卫之后（同事务一致快照；RevertedAt 已被守卫置位）
        var journal = await db.TimeOffsetApplications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == applicationId);
        if (journal == null) { await tx.RollbackAsync(); return null; }

        Dictionary<string, List<JournalIdRange>>? rows;
        try { rows = TimeOffsetJournalCodec.Decode(journal.RowsJson); }
        catch { rows = null; }
        if (rows == null) { await tx.RollbackAsync(); return null; }

        var perTable = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
        foreach (var (key, ranges) in rows)
        {
            if (ranges.Count == 0) continue;
            var plan = FindPlan(key);
            if (plan == null) continue;
            await plan.ShiftAsync(db, ranges, -journal.ShiftSeconds);
            if (!perTable.TryGetValue(plan.Table, out var tableRanges))
                perTable[plan.Table] = tableRanges = [];
            tableRanges.AddRange(ranges);
        }

        var anomaly = await db.TimeAnomalies.FindAsync(journal.AnomalyId);
        if (anomaly != null) anomaly.Status = TimeAnomalyStatus.Reverted;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await tx.DisposeAsync();

        var walTruncated = false;
        try { walTruncated = await SqliteMaintenance.TryTruncateWalAsync(db); }
        catch { }
        if (!walTruncated) ScheduleWalTruncate();

        var restoredRows = perTable.Values
            .Select(TimeOffsetJournalCodec.NormalizeRanges)
            .Sum(ranges => ranges.Sum(range => range.Count));
        return new RestoreResult { RestoredRowsTotal = checked((int)restoredRows) };
    }

    /// <summary>查该异常最近一条未恢复（RevertedAt == null）的应用日志并恢复。</summary>
    public async Task<RestoreResult?> RestoreLatestForAnomaly(long anomalyId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = await db.TimeOffsetApplications.AsNoTracking()
            .Where(x => x.AnomalyId == anomalyId && x.RevertedAt == null)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        if (app == null) return null;
        return await Restore(app.Id);
    }

    private void ScheduleWalTruncate()
    {
        _ = Task.Run(async () =>
        {
            await WalCheckpointGate.WaitAsync();
            try
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        if (await SqliteMaintenance.TryTruncateWalAsync(db)) return;
                    }
                    catch { }
                }
            }
            finally
            {
                WalCheckpointGate.Release();
            }
        });
    }
}
