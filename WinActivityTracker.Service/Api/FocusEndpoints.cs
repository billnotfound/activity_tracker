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
        var start = NormalizeToUtc(from);
        var end = NormalizeToUtc(to);

        return await BuildSummary(db, start, end, tagService);
    }

    private static DateTime NormalizeToUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime()
    };

    private static async Task<IResult> BuildSummary(AppDbContext db, DateTime start, DateTime end, TagService tagService)
    {
        var offPeriods = await GetOffPeriods(db, start, end);
        var rows = await db.FocusChanges
            .AsNoTracking()
            .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess)
            .Where(f => f.Timestamp >= start && f.Timestamp <= end)
            .OrderBy(f => f.Timestamp)
            .Select(f => new SummaryFocusRow
            {
                Timestamp = f.Timestamp,
                ProcessName = f.ProcessName,
                WindowTitle = f.WindowTitle,
                DurationSeconds = f.DurationSeconds
            })
            .ToListAsync();

        // The previous query shape ran three FocusChanges scans, each with a
        // correlated SystemEvents NOT EXISTS. A day with only a few thousand
        // focus rows consequently took several seconds. The range index now
        // feeds one ordered read and all small rule/off-period checks happen in
        // a single linear pass.
        var hiddenRules = tagService.GetHiddenRules();
        var idleRules = tagService.GetIdleRules().Where(rule => rule.Weight >= 10).ToList();
        var buckets = new Dictionary<string, SummaryBucket>(StringComparer.OrdinalIgnoreCase);
        var previousProcess = (string?)null;
        var offIndex = 0;
        double totalIdleSec = 0;
        foreach (var row in rows)
        {
            while (offIndex < offPeriods.Count && offPeriods[offIndex].End <= row.Timestamp)
                offIndex++;
            if (offIndex < offPeriods.Count
                && offPeriods[offIndex].Start <= row.Timestamp
                && row.Timestamp < offPeriods[offIndex].End)
                continue;
            if (TagService.MatchesHidden(hiddenRules, row.ProcessName, row.WindowTitle))
                continue;
            if (TagService.MatchesIdle(idleRules, row.ProcessName, row.WindowTitle))
            {
                totalIdleSec += row.DurationSeconds;
                continue;
            }

            if (!buckets.TryGetValue(row.ProcessName, out var bucket))
                buckets[row.ProcessName] = bucket = new SummaryBucket(row.ProcessName);
            bucket.TotalSeconds += row.DurationSeconds;
            bucket.SwitchCount++;
            if (!string.Equals(previousProcess, row.ProcessName, StringComparison.OrdinalIgnoreCase))
                bucket.AdjustedSwitchCount++;
            previousProcess = row.ProcessName;
        }

        var totalSleepSec = offPeriods.Sum(p => p.DurationSeconds);
        var data = buckets.Values.OrderByDescending(x => x.TotalSeconds).ToList();

        return Results.Ok(new
        {
            items = data.Select(d => new
            {
                d.ProcessName,
                d.TotalSeconds,
                SwitchCount = d.SwitchCount,
                d.AdjustedSwitchCount,
                Tags = tagService.ResolveTags(d.ProcessName, null)
            }),
            totalSleepSeconds = totalSleepSec,
            totalIdleSeconds = totalIdleSec
        });
    }

    private static async Task<List<(DateTime Start, DateTime End, double DurationSeconds)>> GetOffPeriods(
        AppDbContext db, DateTime start, DateTime end)
    {
        var events = await db.SystemEvents
            .AsNoTracking()
            .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown)
                && e.Timestamp >= start && e.Timestamp < end)
            .ToListAsync();
        foreach (var eventType in new[] { SystemEventTypes.Sleep, SystemEventTypes.Shutdown })
        {
            var preceding = await db.SystemEvents.AsNoTracking()
                .Where(e => e.EventType == eventType && e.Timestamp < start)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefaultAsync();
            if (preceding != null) events.Add(preceding);
        }

        return events
            .Select(e =>
            {
                var clippedStart = e.Timestamp < start ? start : e.Timestamp;
                var eventEnd = e.Timestamp.AddSeconds(e.DurationSeconds);
                var clippedEnd = eventEnd > end ? end : eventEnd;
                return (Start: clippedStart, End: clippedEnd,
                    DurationSeconds: (clippedEnd - clippedStart).TotalSeconds);
            })
            .Where(e => e.DurationSeconds > 0)
            .OrderBy(e => e.Start)
            .ToList();
    }
}

internal sealed class SummaryFocusRow
{
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
}

internal sealed class SummaryBucket(string processName)
{
    public string ProcessName { get; } = processName;
    public double TotalSeconds { get; set; }
    public int SwitchCount { get; set; }
    public int AdjustedSwitchCount { get; set; }
}
