// 偏移应用/恢复引擎（Task 8）。
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
// 流水：按列 SELECT 受影响 Id（含每列 MaxRowTimestamp）→ 写 TimeOffsetApplication
// （RowsJson + ShiftSeconds + MaxRowTimestamp，先于 UPDATE 落库）→ ExecuteUpdateAsync
// 按 Id 批量平移（500/批）→ 异常标 Applied（LastAppliedAt）。
// 恢复：按 RowsJson 的列级 Id 列表 −ShiftSeconds 反向平移 → RevertedAt 置位 →
// 异常标 Reverted。双列表按列分开记录 Id，恢复是 apply 的严格逆变换
// （表级 Id 列表无法区分"仅 CloseTime 被平移"的行，故按列记录）。
// scoped DbContext 直用，不用 WriteQueue——批量 UPDATE 不走队列。
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;

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

public class TimeOffsetApplyService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TimeOffsetApplyService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    private enum Scenario { HeartbeatForward, HeartbeatBackward, EventLogPre, EventLogPost }

    /// <summary>一次应用的方向判定结果：平移量 + 范围（含边界是否有界）。</summary>
    private sealed record ShiftPlan(Scenario Scenario, double Shift,
        DateTime? From, DateTime? To, bool FromBounded, bool ToBounded);

    /// <summary>每列的范围谓词 + 平移表达式。Key = 日志键（单列表 = 表名；双列表 = "表.列"）。</summary>
    private sealed record ColumnPlan(
        string Key, string Table,
        Func<AppDbContext, ShiftPlan, Task<(List<long> Ids, DateTime? MaxTs)>> CollectAsync,
        Func<AppDbContext, IReadOnlyCollection<long>, double, Task> ShiftAsync);

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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.FocusChanges.Where(f => batch.Contains(f.Id))
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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.WindowSessions.Where(w => batch.Contains(w.Id))
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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.WindowSessions.Where(w => batch.Contains(w.Id) && w.CloseTime != null)
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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.ProcessSessions.Where(x => batch.Contains(x.Id))
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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.ProcessSessions.Where(x => batch.Contains(x.Id) && x.EndTime != null)
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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.MediaSessionRecords.Where(m => batch.Contains(m.Id))
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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.MediaSessionRecords.Where(m => batch.Contains(m.Id) && m.EndTime != null)
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
            async (db, ids, shift) =>
            {
                foreach (var batch in ids.Chunk(500))
                    await db.SystemEvents.Where(e => batch.Contains(e.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.Timestamp, e => e.Timestamp.AddSeconds(shift)));
            })
    };

    private static ColumnPlan? FindPlan(string key)
        => Array.Find(Plans, x => x.Key == key);

    /// <summary>
    /// 方向判定：Heartbeat 由偏移符号决定（direction 参数忽略）；
    /// EventLog 优先 direction 参数，缺省按 Note 前缀「变化前」→ pre /「变化后」→ post。
    /// 判定不出方向或偏移为零返回 null。
    /// </summary>
    private static ShiftPlan? ResolvePlan(TimeAnomaly anomaly, string? direction, DateTime now)
    {
        if (anomaly.Source == TimeAnomalySources.Heartbeat)
        {
            var offset = anomaly.OffsetSeconds;
            if (offset > 0)
            {
                // 前调：FromWall = 上次心跳 + offset；空洞保证 [FromWall, +∞) 全是污染记录。
                var from = anomaly.FromWall ?? anomaly.DetectedAt;
                return new ShiftPlan(Scenario.HeartbeatForward, -offset, from,
                    anomaly.ToWall ?? now.AddSeconds(offset), FromBounded: true, ToBounded: true);
            }
            if (offset < 0)
            {
                // 回调：重叠段 [FromWall − |offset|, FromWall] best-effort。
                var to = anomaly.FromWall ?? anomaly.DetectedAt;
                return new ShiftPlan(Scenario.HeartbeatBackward, -offset, to.AddSeconds(offset), to,
                    FromBounded: true, ToBounded: true);
            }
            return null;
        }

        if (anomaly.Source == TimeAnomalySources.EventLog)
        {
            var dir = direction ?? (anomaly.Note?.Contains("变化前") == true ? "pre"
                : anomaly.Note?.Contains("变化后") == true ? "post"
                : null);
            var old = anomaly.OldTime ?? anomaly.FromWall;
            var @new = anomaly.NewTime ?? anomaly.ToWall;
            if (string.Equals(dir, "pre", StringComparison.OrdinalIgnoreCase))
            {
                // 变化前记录错：只平移无歧义旧区 wall ≤ min(OldTime, NewTime)。
                var to = old ?? @new ?? anomaly.DetectedAt;
                if (@new != null && @new.Value < to) to = @new.Value;
                return new ShiftPlan(Scenario.EventLogPre, anomaly.OffsetSeconds, null, to,
                    FromBounded: false, ToBounded: true);
            }
            if (string.Equals(dir, "post", StringComparison.OrdinalIgnoreCase))
            {
                // 变化后记录错：只平移无歧义新区 wall ≥ max(OldTime, NewTime)。
                var from = @new ?? old ?? anomaly.DetectedAt;
                if (old != null && old.Value > from) from = old.Value;
                return new ShiftPlan(Scenario.EventLogPost, -anomaly.OffsetSeconds, from, null,
                    FromBounded: true, ToBounded: false);
            }
        }
        return null;
    }

    /// <summary>预览：每表受影响行数（direction 可空——Pending 方向选择时必填）。</summary>
    public ApplyPreview? Preview(long anomalyId, string? direction = null)
        => PreviewAsync(anomalyId, direction).GetAwaiter().GetResult();

    public async Task<ApplyPreview?> PreviewAsync(long anomalyId, string? direction = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var anomaly = await db.TimeAnomalies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == anomalyId);
        if (anomaly == null) return null;
        var plan = ResolvePlan(anomaly, direction, DateTime.UtcNow);
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
    /// 应用偏移。direction 仅 EventLog Pending 必填（"pre"/"post"）；
    /// 状态必须是 Confirmed/Pending/Reverted（Applied 拒绝重复应用）。
    /// 返回 null 表示：异常不存在 / 状态不允许 / 方向不可判定 / 无可平移行。
    ///
    /// 并发封死（Round 2）：事务内第一步做守卫 UPDATE（先取写锁 + 状态条件）。
    /// Microsoft.Data.Sqlite 默认 busy_timeout 30s——竞争方通常等待而非抛错，若先采集
    /// 行再写会基于陈旧行 ID 快照继续 → 双重平移。守卫 UPDATE 让竞争方先拿写锁：
    /// 等到胜者提交后再执行，因状态已非 Confirmed/Pending 而 affected==0，
    /// 直接回滚返回 null，不再采集/平移/写 journal；状态读取与行采集移入守卫之后，
    /// 保证基于提交后状态的一致快照。
    /// </summary>
    public async Task<ApplyResult?> Apply(long anomalyId, string? direction)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();

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
        var plan = ResolvePlan(anomaly, direction, DateTime.UtcNow);
        if (plan == null || plan.Shift == 0) { await tx.RollbackAsync(); return null; }

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

        // Fix 4 保持：无可平移行 → Applied 改置 Ignored（避免每次启动自动应用反复空处理）
        if (rowsJson.Count == 0)
        {
            await db.TimeAnomalies
                .Where(a => a.Id == anomalyId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, TimeAnomalyStatus.Ignored));
            await tx.CommitAsync();
            return null;
        }

        // 2) 日志先落库：UPDATE 失败时恢复凭据已在（同一事务，异常即回滚）
        var journal = new TimeOffsetApplication
        {
            AnomalyId = anomaly.Id,
            AppliedAt = DateTime.UtcNow,
            ShiftSeconds = plan.Shift,
            RowsJson = JsonSerializer.Serialize(rowsJson),
            // Fix 2：平移后最大时间戳——正偏移修复（回调 / EventLog pre δ>0）后，
            // 引用行的新时间戳 ≥ 该值；清理协同按 < cutoff 删除，避免误删仍需恢复凭据的日志。
            MaxRowTimestamp = (maxTs ?? DateTime.UtcNow).AddSeconds(plan.Shift)
        };
        db.TimeOffsetApplications.Add(journal);
        await db.SaveChangesAsync();

        // 3) 按 Id 批量平移（500/批）——WHERE 只按 Id 圈定：日志与实际平移严格一致
        foreach (var p in Plans)
            if (rowsJson.TryGetValue(p.Key, out var ids))
                await p.ShiftAsync(db, ids, plan.Shift);

        // R2-3：状态已由守卫置 Applied，这里只补 LastAppliedAt（不再重复置状态）
        await db.TimeAnomalies
            .Where(a => a.Id == anomalyId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastAppliedAt, DateTime.UtcNow));

        await tx.CommitAsync();

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
    /// 恢复：按 RowsJson 的列级 Id 列表 −ShiftSeconds 反向平移（严格逆变换），
    /// RevertedAt 置位，异常标 Reverted。未知键（未来新增表）跳过不阻断。
    /// 整体包事务：RevertedAt 检查与反向平移同事务，闭合双重恢复竞态——
    /// SQLite 写锁串行化并发 Restore，后者写冲突抛异常，不会静默二次平移。
    ///
    /// 并发封死（Ruling B4，与 Apply 相同的写优先守卫）：事务内第一步做守卫 UPDATE
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

        Dictionary<string, List<long>>? rows;
        try { rows = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(journal.RowsJson); }
        catch { rows = null; }
        if (rows == null) { await tx.RollbackAsync(); return null; }

        var perTable = new Dictionary<string, HashSet<long>>();
        foreach (var (key, ids) in rows)
        {
            if (ids.Count == 0) continue;
            var plan = FindPlan(key);
            if (plan == null) continue;
            await plan.ShiftAsync(db, ids, -journal.ShiftSeconds);
            if (!perTable.TryGetValue(plan.Table, out var set)) perTable[plan.Table] = set = new HashSet<long>();
            set.UnionWith(ids);
        }

        var anomaly = await db.TimeAnomalies.FindAsync(journal.AnomalyId);
        if (anomaly != null) anomaly.Status = TimeAnomalyStatus.Reverted;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new RestoreResult { RestoredRowsTotal = perTable.Values.Sum(s => s.Count) };
    }

    /// <summary>查该异常最近一条未恢复（RevertedAt == null）的应用日志并恢复。供 Task 9 API 调用。</summary>
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
}
