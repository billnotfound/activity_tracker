using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Service.Api;

public static class WindowEndpoints
{
    public static void MapWindowEndpoints(this WebApplication app)
    {
        app.MapGet("/api/windows/current", GetCurrentWindows);
        app.MapGet("/api/windows/timeline", GetTimeline);
        app.MapGet("/api/windows/sessions", GetWindowSessions);
        app.MapGet("/api/system/events", GetSystemEvents);
    }

    private static IResult GetCurrentWindows(SettingsService settings, ProcessNameCache processCache, TagService tagService)
    {
        var excluded = settings.Settings.ExcludedProcesses;
        var hidden = tagService.GetHiddenRules();
        // Only strong idle rules (weight >= 10) affect live windows; weak rules
        // are overridden by foreground activity.
        var idle = tagService.GetIdleRules().Where(r => r.Weight >= 10).ToList();
        var windows = WindowTracker.EnumerateVisibleWindows(processCache);
        return Results.Ok(windows
            .Where(w => !excluded.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase)
                && !TagService.MatchesHidden(hidden, w.ProcessName, w.Title)
                && !TagService.MatchesIdle(idle, w.ProcessName, w.Title))
            .Select(w => new
            {
                w.ProcessName,
                w.Title,
                w.IsFocused
            }));
    }

    private static async Task<IResult> GetTimeline(
        DateTime? from, DateTime? to, int? limit, int? offset, AppDbContext db, TagService tagService)
    {
        var start = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow.AddHours(-1);
        var end = to.HasValue
            ? DateTime.SpecifyKind(to.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow;

        var take = Math.Clamp(limit ?? 2000, 1, 50000); // Increased max to 50k for longer ranges
        var skip = Math.Max(0, offset ?? 0);

        var baseQuery = HiddenFilter.ExcludeHidden(
            db.FocusChanges.AsNoTracking().Where(f => f.Timestamp >= start && f.Timestamp <= end),
            tagService.GetHiddenRules());

        // Idle-tagged rows are dropped so the frontend's gap detection renders
        // those spans as idle (gray) areas instead of activity.
        baseQuery = IdleFilter.ExcludeIdle(baseQuery, tagService.GetIdleRules());

        var total = await baseQuery.CountAsync();

        // Sampling for wide ranges: the frontend renders at most MAX_POINTS
        // (8000) points, so shipping up to 50k rows is mostly wasted bytes.
        // Fetch all matching rows (hidden rules already applied), then keep
        // every Nth in memory so the chart still spans the whole range evenly.
        // Only for the full-range fetch (skip == 0); explicit pagination keeps
        // its exact semantics.
        const int maxPoints = 8000;
        var sampled = false;
        List<TimelineRow> rows;
        if (total > maxPoints && skip == 0)
        {
            var all = await baseQuery
                .OrderBy(f => f.Timestamp)
                .Select(f => new TimelineRow
                {
                    Timestamp = f.Timestamp,
                    ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle,
                    DurationSeconds = f.DurationSeconds
                })
                .ToListAsync();
            var step = (total + maxPoints - 1) / maxPoints;
            sampled = true;
            rows = [];
            for (var i = 0; i < all.Count; i += step)
                rows.Add(all[i]);
            // Anchor the last row so the chart reaches the range end even if
            // the stride misses it.
            if (all.Count > 0 && rows[^1] != all[^1])
                rows.Add(all[^1]);
            skip = 0;
            take = rows.Count;
        }
        else
        {
            rows = await baseQuery
                .OrderBy(f => f.Timestamp)
                .Skip(skip)
                .Take(take)
                .Select(f => new TimelineRow
                {
                    Timestamp = f.Timestamp,
                    ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle,
                    DurationSeconds = f.DurationSeconds
                })
                .ToListAsync();
        }

        var data = rows.Select(r => new
        {
            r.Timestamp,
            r.ProcessName,
            r.WindowTitle,
            r.DurationSeconds,
            Tags = tagService.ResolveTags(r.ProcessName, r.WindowTitle)
        }).ToList();

        return Results.Ok(new { data, total, offset = skip, limit = take, sampled });
    }

    private static async Task<IResult> GetWindowSessions(
        DateTime? from, DateTime? to, int? limit, AppDbContext db, TagService tagService)
    {
        var start = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow.AddHours(-1);
        var end = to.HasValue
            ? DateTime.SpecifyKind(to.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow;

        var take = Math.Clamp(limit ?? 5000, 1, 50000);

        // Get window sessions that overlap with the time range
        var sessions = await IdleFilter.ExcludeIdle(
                HiddenFilter.ExcludeHidden(
                    db.WindowSessions.AsNoTracking()
                        .Where(w => w.OpenTime <= end && (w.CloseTime == null || w.CloseTime >= start)),
                    tagService.GetHiddenRules()),
                tagService.GetIdleRules())
            .OrderBy(w => w.OpenTime)
            .Take(take)
            .Select(w => new
            {
                w.ProcessName,
                w.WindowTitle,
                w.OpenTime,
                w.CloseTime
            })
            .ToListAsync();

        return Results.Ok(sessions);
    }

    private static async Task<IResult> GetSystemEvents(
        DateTime? from, DateTime? to, AppDbContext db)
    {
        var start = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow.AddDays(-7);
        var end = to.HasValue
            ? DateTime.SpecifyKind(to.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow;

        var events = await db.SystemEvents
            .AsNoTracking()
            .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown || e.EventType == SystemEventTypes.Idle)
                && e.Timestamp < end)
            .OrderBy(e => e.Timestamp)
            .Select(e => new
            {
                e.EventType,
                e.Timestamp,
                e.DurationSeconds
            })
            .ToListAsync();

        // Filter in-memory: keep only events that actually overlap [start, end].
        // A sleep starting before 'start' but lasting into the range must be included
        // so the frontend can draw the partial dashed box.
        events = events
            .Where(e => e.Timestamp.AddSeconds(e.DurationSeconds) > start)
            .ToList();

        return Results.Ok(events);
    }
}

internal sealed class TimelineRow
{
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
}
