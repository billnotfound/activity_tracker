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

    private sealed record ShiftPlan(Scenario Scenario, double Shift,
        DateTime? From, DateTime? To, bool FromBounded, bool ToBounded);

    private sealed record ColumnSelection(
        List<JournalIdRange> Ranges, DateTime? MaxTimestamp);

    private sealed class TimestampedId
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private sealed record ColumnPlan(
        string Key, string Table,
        Func<AppDbContext, ShiftPlan, Task<ColumnSelection>> CollectAsync,
        Func<AppDbContext, IReadOnlyList<JournalIdRange>, double, Task> ShiftAsync);

    private static readonly ColumnPlan[] Plans =
    {
        new("FocusChanges", "FocusChanges",
            async (db, p) =>
            {
                IQueryable<FocusChange> q = db.FocusChanges.AsNoTracking();
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(f => f.Timestamp >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(f => f.Timestamp <= tv);
                }
                return await CollectRangesAsync(q.Select(f => new TimestampedId
                {
                    Id = f.Id,
                    Timestamp = f.Timestamp
                }));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.FocusChanges.Where(f => f.Id >= range.First && f.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(f => f.Timestamp, f => f.Timestamp.AddSeconds(shift)));
            }),

        new("WindowSessions.OpenTime", "WindowSessions",
            async (db, p) =>
            {
                IQueryable<WindowSession> q = db.WindowSessions.AsNoTracking();
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(w => w.OpenTime >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(w => w.OpenTime <= tv);
                }
                return await CollectRangesAsync(q.Select(w => new TimestampedId
                {
                    Id = w.Id,
                    Timestamp = w.OpenTime
                }));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.WindowSessions.Where(w => w.Id >= range.First && w.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(w => w.OpenTime, w => w.OpenTime.AddSeconds(shift)));
            }),

        new("WindowSessions.CloseTime", "WindowSessions",
            async (db, p) =>
            {
                IQueryable<WindowSession> q = db.WindowSessions.AsNoTracking().Where(w => w.CloseTime != null);
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(w => w.CloseTime >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(w => w.CloseTime <= tv);
                }
                return await CollectRangesAsync(q.Select(w => new TimestampedId
                {
                    Id = w.Id,
                    Timestamp = w.CloseTime!.Value
                }));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.WindowSessions.Where(w => w.Id >= range.First && w.Id <= range.Last && w.CloseTime != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(w => w.CloseTime, w => w.CloseTime!.Value.AddSeconds(shift)));
            }),

        new("ProcessSessions.StartTime", "ProcessSessions",
            async (db, p) =>
            {
                IQueryable<ProcessSession> q = db.ProcessSessions.AsNoTracking();
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(x => x.StartTime >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(x => x.StartTime <= tv);
                }
                return await CollectRangesAsync(q.Select(x => new TimestampedId
                {
                    Id = x.Id,
                    Timestamp = x.StartTime
                }));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.ProcessSessions.Where(x => x.Id >= range.First && x.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartTime, x => x.StartTime.AddSeconds(shift)));
            }),

        new("ProcessSessions.EndTime", "ProcessSessions",
            async (db, p) =>
            {
                IQueryable<ProcessSession> q = db.ProcessSessions.AsNoTracking().Where(x => x.EndTime != null);
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(x => x.EndTime >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(x => x.EndTime <= tv);
                }
                return await CollectRangesAsync(q.Select(x => new TimestampedId
                {
                    Id = x.Id,
                    Timestamp = x.EndTime!.Value
                }));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.ProcessSessions.Where(x => x.Id >= range.First && x.Id <= range.Last && x.EndTime != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndTime, x => x.EndTime!.Value.AddSeconds(shift)));
            }),

        new("MediaSessionRecords.StartTime", "MediaSessionRecords",
            async (db, p) =>
            {
                IQueryable<MediaSessionRecord> q = db.MediaSessionRecords.AsNoTracking();
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(m => m.StartTime >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(m => m.StartTime <= tv);
                }
                return await CollectRangesAsync(q.Select(m => new TimestampedId
                {
                    Id = m.Id,
                    Timestamp = m.StartTime
                }));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.MediaSessionRecords.Where(m => m.Id >= range.First && m.Id <= range.Last)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, m => m.StartTime.AddSeconds(shift)));
            }),

        new("MediaSessionRecords.EndTime", "MediaSessionRecords",
            async (db, p) =>
            {
                IQueryable<MediaSessionRecord> q = db.MediaSessionRecords.AsNoTracking().Where(m => m.EndTime != null);
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(m => m.EndTime >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(m => m.EndTime <= tv);
                }
                return await CollectRangesAsync(q.Select(m => new TimestampedId
                {
                    Id = m.Id,
                    Timestamp = m.EndTime!.Value
                }));
            },
            async (db, ranges, shift) =>
            {
                foreach (var range in ranges)
                    await db.MediaSessionRecords.Where(m => m.Id >= range.First && m.Id <= range.Last && m.EndTime != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.EndTime, m => m.EndTime!.Value.AddSeconds(shift)));
            }),

        new("SystemEvents", "SystemEvents",
            async (db, p) =>
            {
                IQueryable<SystemEvent> q = db.SystemEvents.AsNoTracking();
                if (p.FromBounded)
                {
                    var fv = p.From!.Value;
                    q = q.Where(e => e.Timestamp >= fv);
                }
                if (p.ToBounded)
                {
                    var tv = p.To!.Value;
                    q = q.Where(e => e.Timestamp <= tv);
                }
                return await CollectRangesAsync(q.Select(e => new TimestampedId
                {
                    Id = e.Id,
                    Timestamp = e.Timestamp
                }));
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

    private static async Task<ColumnSelection> CollectRangesAsync(
        IQueryable<TimestampedId> query)
    {
        var ranges = new List<JournalIdRange>();
        DateTime? maxTimestamp = null;

        await foreach (var row in query.OrderBy(x => x.Id).AsAsyncEnumerable())
        {
            if (ranges.Count > 0
                && ranges[^1].Last != long.MaxValue
                && row.Id == ranges[^1].Last + 1)
            {
                ranges[^1] = ranges[^1] with { Last = row.Id };
            }
            else
            {
                ranges.Add(new JournalIdRange(row.Id, row.Id));
            }

            if (maxTimestamp == null || row.Timestamp > maxTimestamp)
                maxTimestamp = row.Timestamp;
        }

        return new ColumnSelection(ranges, maxTimestamp);
    }

    private static void AddTableRanges(
        Dictionary<string, List<JournalIdRange>> perTable,
        string table,
        IEnumerable<JournalIdRange> ranges)
    {
        if (!perTable.TryGetValue(table, out var tableRanges))
            perTable[table] = tableRanges = [];
        tableRanges.AddRange(ranges);
    }

    private static Dictionary<string, int> BuildTableCounts(
        Dictionary<string, List<JournalIdRange>> perTable) =>
        perTable.ToDictionary(
            pair => pair.Key,
            pair => checked((int)TimeOffsetJournalCodec.NormalizeRanges(pair.Value)
                .Sum(range => range.Count)),
            StringComparer.Ordinal);

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

        var perTable = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
        foreach (var p in Plans)
        {
            var selection = await p.CollectAsync(db, plan);
            if (selection.Ranges.Count == 0) continue;
            AddTableRanges(perTable, p.Table, selection.Ranges);
        }
        return new ApplyPreview
        {
            TableCounts = BuildTableCounts(perTable)
        };
    }

    public async Task<ApplyPreview?> PreviewManualRangeAsync(
        DateTime from, DateTime to, double shiftSeconds)
    {
        if (to <= from || shiftSeconds == 0 || !double.IsFinite(shiftSeconds)) return null;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = new ShiftPlan(Scenario.EventLogPost, shiftSeconds, from, to, true, true);
        var perTable = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
        foreach (var column in Plans)
        {
            var selection = await column.CollectAsync(db, plan);
            if (selection.Ranges.Count == 0) continue;
            AddTableRanges(perTable, column.Table, selection.Ranges);
        }
        return new ApplyPreview
        {
            TableCounts = BuildTableCounts(perTable)
        };
    }

    public async Task<ApplyResult?> ApplyManualRangeAsync(
        DateTime from, DateTime to, double shiftSeconds, string? note = null)
    {
        if (to <= from || shiftSeconds == 0 || !double.IsFinite(shiftSeconds)) return null;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            $"PRAGMA journal_size_limit={SqliteMaintenance.JournalSizeLimitBytes}");

        var anomaly = new TimeAnomaly
        {
            DetectedAt = DateTime.UtcNow,
            Source = TimeAnomalySources.User,
            OffsetSeconds = -shiftSeconds,
            Status = TimeAnomalyStatus.Applied,
            Direction = "manual",
            FromWall = from,
            ToWall = to,
            Note = string.IsNullOrWhiteSpace(note) ? "User-selected time correction" : note.Trim(),
            LastAppliedAt = DateTime.UtcNow
        };
        db.TimeAnomalies.Add(anomaly);
        await db.SaveChangesAsync();

        var plan = new ShiftPlan(Scenario.EventLogPost, shiftSeconds, from, to, true, true);
        var compactRows = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
        var perTable = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
        DateTime? maxTs = from;
        foreach (var column in Plans)
        {
            var selection = await column.CollectAsync(db, plan);
            if (selection.Ranges.Count == 0) continue;
            compactRows[column.Key] = selection.Ranges;
            AddTableRanges(perTable, column.Table, selection.Ranges);
            if (selection.MaxTimestamp != null
                && (maxTs == null || selection.MaxTimestamp > maxTs))
                maxTs = selection.MaxTimestamp;
        }

        if (compactRows.Count == 0)
        {
            await tx.RollbackAsync();
            return null;
        }

        db.TimeOffsetApplications.Add(new TimeOffsetApplication
        {
            AnomalyId = anomaly.Id,
            AppliedAt = DateTime.UtcNow,
            ShiftSeconds = shiftSeconds,
            RowsJson = TimeOffsetJournalCodec.Encode(compactRows),
            MaxRowTimestamp = (maxTs ?? to).AddSeconds(shiftSeconds)
        });
        await db.SaveChangesAsync();

        foreach (var column in Plans)
            if (compactRows.TryGetValue(column.Key, out var ranges))
                await column.ShiftAsync(db, ranges, shiftSeconds);

        await tx.CommitAsync();
        await tx.DisposeAsync();

        var walTruncated = false;
        try { walTruncated = await SqliteMaintenance.TryTruncateWalAsync(db); }
        catch { }
        if (!walTruncated) ScheduleWalTruncate();

        var tableCounts = BuildTableCounts(perTable);
        return new ApplyResult
        {
            AnomalyId = anomaly.Id,
            ShiftSeconds = shiftSeconds,
            TableCounts = tableCounts,
            AppliedRowsTotal = tableCounts.Values.Sum()
        };
    }

    public async Task<ApplyResult?> Apply(long anomalyId, string? direction = null,
        TimeReferenceResult? ntp = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            $"PRAGMA journal_size_limit={SqliteMaintenance.JournalSizeLimitBytes}");

        // The first write acquires SQLite's write lock before target collection,
        // preventing concurrent callers from shifting the same snapshot twice.
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

        var anomaly = await db.TimeAnomalies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == anomalyId);
        if (anomaly == null) { await tx.RollbackAsync(); return null; }
        var plan = ResolvePlan(anomaly, direction, ntp, settings.Settings.NtpEpsilonSeconds, DateTime.UtcNow);
        if (plan == null || plan.Shift == 0) { await tx.RollbackAsync(); return null; }

        await db.TimeAnomalies
            .Where(a => a.Id == anomalyId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Direction, ScenarioDirection(plan)));

        var compactRows = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
        var perTable = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
        var maxTs = plan.From;
        foreach (var p in Plans)
        {
            var selection = await p.CollectAsync(db, plan);
            if (selection.Ranges.Count == 0) continue;
            compactRows[p.Key] = selection.Ranges;
            AddTableRanges(perTable, p.Table, selection.Ranges);
            if (selection.MaxTimestamp != null
                && (maxTs == null || selection.MaxTimestamp > maxTs))
                maxTs = selection.MaxTimestamp;
        }

        if (compactRows.Count == 0)
        {
            await tx.RollbackAsync();
            return null;
        }

        var journal = new TimeOffsetApplication
        {
            AnomalyId = anomaly.Id,
            AppliedAt = DateTime.UtcNow,
            ShiftSeconds = plan.Shift,
            RowsJson = TimeOffsetJournalCodec.Encode(compactRows),
            MaxRowTimestamp = (maxTs ?? DateTime.UtcNow).AddSeconds(plan.Shift)
        };
        db.TimeOffsetApplications.Add(journal);
        await db.SaveChangesAsync();

        foreach (var p in Plans)
            if (compactRows.TryGetValue(p.Key, out var ranges))
                await p.ShiftAsync(db, ranges, plan.Shift);

        await db.TimeAnomalies
            .Where(a => a.Id == anomalyId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastAppliedAt, DateTime.UtcNow));

        await tx.CommitAsync();
        await tx.DisposeAsync();

        var walTruncated = false;
        try { walTruncated = await SqliteMaintenance.TryTruncateWalAsync(db); }
        catch { }
        if (!walTruncated) ScheduleWalTruncate();

        var tableCounts = BuildTableCounts(perTable);
        return new ApplyResult
        {
            AnomalyId = anomaly.Id,
            ShiftSeconds = plan.Shift,
            TableCounts = tableCounts,
            AppliedRowsTotal = tableCounts.Values.Sum()
        };
    }

    public async Task<RestoreResult?> Restore(long applicationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Acquire the write lock before reading the journal so only one restore wins.
        var affected = await db.TimeOffsetApplications
            .Where(x => x.Id == applicationId && x.RevertedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevertedAt, DateTime.UtcNow));
        if (affected == 0)
        {
            await tx.RollbackAsync();
            return null;
        }

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
