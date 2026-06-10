using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class FocusEndpoints
{
    public static void MapFocusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/summary/today", GetTodaySummary);
        app.MapGet("/api/summary/range", GetRangeSummary);
    }

    private static async Task<IResult> GetTodaySummary(string? date, AppDbContext db, TagService tagService)
    {
        if (date != null && !DateOnly.TryParse(date, out _))
            return Results.BadRequest(new { error = "Invalid date format, use yyyy-MM-dd" });

        var targetDate = date != null
            ? DateOnly.Parse(date)
            : DateOnly.FromDateTime(DateTime.Now);

        var localStart = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var localEnd = targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local);
        var start = localStart.ToUniversalTime();
        var end = localEnd.ToUniversalTime();

        return await BuildSummary(db, start, end, tagService);
    }

    private static async Task<IResult> GetRangeSummary(DateTime from, DateTime to, AppDbContext db, TagService tagService)
    {
        var start = DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(to, DateTimeKind.Local).ToUniversalTime();

        return await BuildSummary(db, start, end, tagService);
    }

    private static async Task<IResult> BuildSummary(AppDbContext db, DateTime start, DateTime end, TagService tagService)
    {
        var offPeriods = await GetOffPeriods(db, start, end);

        // Exclude records whose timestamp falls inside a sleep/shutdown window.
        // Applied BEFORE GroupBy so TotalSeconds and SwitchCount both exclude sleep time.
        var filtered = ExcludeOffPeriods(
            db.FocusChanges
                .AsNoTracking()
                .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess)
                .Where(f => f.Timestamp >= start && f.Timestamp <= end),
            offPeriods);

        var data = await filtered
            .GroupBy(f => f.ProcessName)
            .Select(g => new
            {
                ProcessName = g.Key,
                TotalSeconds = g.Sum(f => f.DurationSeconds),
                SwitchCount = g.Count()
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToListAsync();

        var adj = await ComputeAdjustedSwitchCounts(db, start, end, offPeriods);

        var totalSleepSec = offPeriods.Sum(p => p.DurationSeconds);

        return Results.Ok(new
        {
            items = data.Select(d => new
            {
                d.ProcessName,
                d.TotalSeconds,
                SwitchCount = d.SwitchCount,
                AdjustedSwitchCount = adj.GetValueOrDefault(d.ProcessName, d.SwitchCount),
                Tags = tagService.ResolveTags(d.ProcessName, null)
            }).OrderByDescending(x => x.TotalSeconds),
            totalSleepSeconds = totalSleepSec
        });
    }

    private static async Task<List<(DateTime Start, DateTime End, double DurationSeconds)>> GetOffPeriods(
        AppDbContext db, DateTime start, DateTime end)
    {
        var events = await db.SystemEvents
            .AsNoTracking()
            .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown)
                && e.Timestamp >= start && e.Timestamp <= end)
            .ToListAsync();

        return events
            .Select(e => (Start: e.Timestamp, End: e.Timestamp.AddSeconds(e.DurationSeconds), e.DurationSeconds))
            .ToList();
    }

    /// <summary>
    /// Adds WHERE clauses to exclude FocusChange records whose Timestamp falls
    /// inside any sleep/shutdown window. Uses De Morgan's law:
    ///   NOT (Timestamp >= Start AND Timestamp < End)
    ///   = Timestamp < Start OR Timestamp >= End
    /// Written as explicit OR so EF Core translates it reliably to SQLite.
    /// </summary>
    private static IQueryable<FocusChange> ExcludeOffPeriods(
        IQueryable<FocusChange> query,
        List<(DateTime Start, DateTime End, double DurationSeconds)> offPeriods)
    {
        foreach (var p in offPeriods)
            query = query.Where(f => f.Timestamp < p.Start || f.Timestamp >= p.End);
        return query;
    }

    private static async Task<Dictionary<string, int>> ComputeAdjustedSwitchCounts(
        AppDbContext db, DateTime start, DateTime end,
        List<(DateTime Start, DateTime End, double DurationSeconds)> offPeriods)
    {
        var filtered = ExcludeOffPeriods(
            db.FocusChanges
                .AsNoTracking()
                .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess)
                .Where(f => f.Timestamp >= start && f.Timestamp <= end),
            offPeriods);

        var timestamps = await filtered
            .OrderBy(f => f.Timestamp)
            .Select(f => new { f.Timestamp, f.ProcessName })
            .ToListAsync();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? prev = null;
        foreach (var r in timestamps)
        {
            if (r.ProcessName != prev)
                counts[r.ProcessName] = counts.GetValueOrDefault(r.ProcessName) + 1;
            prev = r.ProcessName;
        }
        return counts;
    }
}
