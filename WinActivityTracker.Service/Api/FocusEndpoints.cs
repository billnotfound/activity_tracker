using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Service.Api;

public static class FocusEndpoints
{
    public static void MapFocusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/summary/today", GetTodaySummary);
        app.MapGet("/api/summary/range", GetRangeSummary);
    }

    private static async Task<IResult> GetTodaySummary(string? date, AppDbContext db)
    {
        var targetDate = date != null
            ? DateOnly.Parse(date)
            : DateOnly.FromDateTime(DateTime.Now);

        var localStart = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var localEnd = targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local);
        var start = localStart.ToUniversalTime();
        var end = localEnd.ToUniversalTime();

        return await BuildSummary(db, start, end);
    }

    private static async Task<IResult> GetRangeSummary(DateTime from, DateTime to, AppDbContext db)
    {
        var start = DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(to, DateTimeKind.Local).ToUniversalTime();

        return await BuildSummary(db, start, end);
    }

    private static async Task<IResult> BuildSummary(AppDbContext db, DateTime start, DateTime end)
    {
        // Off periods (sleep/shutdown) — always just a few rows.
        var offPeriods = await GetOffPeriods(db, start, end);

        // GroupBy + aggregation in SQL — returns ~30 rows instead of 10K+.
        var data = await db.FocusChanges
            .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess)
            .Where(f => f.Timestamp >= start && f.Timestamp <= end)
            .GroupBy(f => f.ProcessName)
            .Select(g => new
            {
                ProcessName = g.Key,
                TotalSeconds = g.Sum(f => f.DurationSeconds),
                SwitchCount = g.Count()
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToListAsync();

        // Adjusted switch counts: merge consecutive same-process switches.
        // Only load Timestamp+ProcessName (not the full entity) to keep this lightweight.
        var adj = await ComputeAdjustedSwitchCounts(db, start, end, offPeriods);

        var totalSleepSec = offPeriods.Sum(p => p.DurationSeconds);

        return Results.Ok(new
        {
            items = data.Select(d => new
            {
                d.ProcessName,
                d.TotalSeconds,
                SwitchCount = d.SwitchCount,
                AdjustedSwitchCount = adj.GetValueOrDefault(d.ProcessName, d.SwitchCount)
            }).OrderByDescending(x => x.TotalSeconds),
            totalSleepSeconds = totalSleepSec
        });
    }

    private static async Task<List<(DateTime Start, DateTime End, double DurationSeconds)>> GetOffPeriods(
        AppDbContext db, DateTime start, DateTime end)
    {
        var events = await db.SystemEvents
            .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown)
                && e.Timestamp >= start && e.Timestamp <= end)
            .ToListAsync();

        return events
            .Select(e => (Start: e.Timestamp, End: e.Timestamp.AddSeconds(e.DurationSeconds), e.DurationSeconds))
            .ToList();
    }

    private static async Task<Dictionary<string, int>> ComputeAdjustedSwitchCounts(
        AppDbContext db, DateTime start, DateTime end,
        List<(DateTime Start, DateTime End, double DurationSeconds)> offPeriods)
    {
        var timestamps = await db.FocusChanges
            .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess)
            .Where(f => f.Timestamp >= start && f.Timestamp <= end)
            .OrderBy(f => f.Timestamp)
            .Select(f => new { f.Timestamp, f.ProcessName })
            .ToListAsync();

        // Exclude records whose timestamp falls within a sleep/shutdown window.
        var filtered = offPeriods.Count == 0
            ? timestamps
            : timestamps
                .Where(t => !offPeriods.Any(p => t.Timestamp >= p.Start && t.Timestamp < p.End))
                .ToList();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? prev = null;
        foreach (var r in filtered)
        {
            if (r.ProcessName != prev)
                counts[r.ProcessName] = counts.GetValueOrDefault(r.ProcessName) + 1;
            prev = r.ProcessName;
        }
        return counts;
    }
}
