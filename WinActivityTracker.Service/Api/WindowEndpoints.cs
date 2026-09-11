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

    private static IResult GetCurrentWindows(SettingsService settings, ProcessNameCache processCache,
        TagService tagService, TitleNormalizer normalizer, bool raw = false)
    {
        var excluded = settings.Settings.ExcludedProcesses;
        var hidden = tagService.GetHiddenRules();
        // Only strong idle rules (weight >= 10) affect live windows; weak rules
        // are overridden by foreground activity.
        var idle = tagService.GetIdleRules().Where(r => r.Weight >= 10).ToList();
        var snap = normalizer.CreateSnapshot();
        var windows = WindowTracker.EnumerateVisibleWindows(processCache);
        return Results.Ok(windows
            .Select(w => (w.ProcessName, Title: raw ? w.Title : snap.Apply(w.ProcessName, w.Title), w.IsFocused))
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
        DateTime? from, DateTime? to, int? limit, int? offset,
        AppDbContext db, TagService tagService, TitleNormalizer normalizer, bool raw = false)
    {
        var start = from.HasValue ? NormalizeToUtc(from.Value) : DateTime.UtcNow.AddHours(-1);
        var end = to.HasValue ? NormalizeToUtc(to.Value) : DateTime.UtcNow;

        var take = Math.Clamp(limit ?? 2000, 1, 50000); // 50k max supports longer ranges
        var skip = Math.Max(0, offset ?? 0);

        var baseQuery = HiddenFilter.ExcludeHidden(
            db.FocusChanges.AsNoTracking().Where(f => f.Timestamp >= start && f.Timestamp <= end),
            tagService.GetHiddenRules());

        // Idle rows are dropped so the frontend's gap detection renders
        // those spans as idle areas instead of activity.
        baseQuery = IdleFilter.ExcludeIdle(baseQuery, tagService.GetIdleRules());

        var total = await baseQuery.CountAsync();

        // Wide-range sampling: keep the work and allocation bounded in SQLite.
        // The former implementation materialized every matching row and then
        // discarded most of them in memory, making week-sized ranges regress as
        // the database grew. Activity ids are monotonic and dense, so modulo-id
        // sampling gives stable coverage; explicit first/last anchors preserve
        // both ends of the selected range.
        const int maxPoints = 8000;
        var sampled = false;
        List<TimelineRow> rows;
        if (total > maxPoints && skip == 0)
        {
            var step = (total + maxPoints - 1) / maxPoints;
            rows = await baseQuery
                .Where(f => f.Id % step == 0)
                .OrderBy(f => f.Timestamp)
                .Take(maxPoints - 2)
                .Select(f => new TimelineRow
                {
                    Id = f.Id,
                    Timestamp = f.Timestamp,
                    ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle,
                    DurationSeconds = f.DurationSeconds
                })
                .ToListAsync();

            var first = await baseQuery.OrderBy(f => f.Timestamp)
                .Select(f => new TimelineRow
                {
                    Id = f.Id, Timestamp = f.Timestamp, ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle, DurationSeconds = f.DurationSeconds
                })
                .FirstAsync();
            var last = await baseQuery.OrderByDescending(f => f.Timestamp)
                .Select(f => new TimelineRow
                {
                    Id = f.Id, Timestamp = f.Timestamp, ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle, DurationSeconds = f.DurationSeconds
                })
                .FirstAsync();
            if (rows.All(row => row.Id != first.Id)) rows.Add(first);
            if (rows.All(row => row.Id != last.Id)) rows.Add(last);
            rows.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            sampled = true;
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
                    Id = f.Id,
                    Timestamp = f.Timestamp,
                    ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle,
                    DurationSeconds = f.DurationSeconds
                })
                .ToListAsync();
        }

        var snap = normalizer.CreateSnapshot();
        var data = rows.Select(r =>
        {
            var title = raw ? r.WindowTitle : snap.Apply(r.ProcessName, r.WindowTitle);
            return new
            {
                r.Timestamp,
                r.ProcessName,
                WindowTitle = title,
                r.DurationSeconds,
                Tags = tagService.ResolveTags(r.ProcessName, title)
            };
        }).ToList();

        return Results.Ok(new { data, total, offset = skip, limit = take, sampled });
    }

    private static async Task<IResult> GetWindowSessions(
        DateTime? from, DateTime? to, int? limit,
        AppDbContext db, TagService tagService, TitleNormalizer normalizer, bool raw = false)
    {
        var start = from.HasValue ? NormalizeToUtc(from.Value) : DateTime.UtcNow.AddHours(-1);
        var end = to.HasValue ? NormalizeToUtc(to.Value) : DateTime.UtcNow;

        var take = Math.Clamp(limit ?? 5000, 1, 50000);

        var sessions = await IdleFilter.ExcludeIdle(
                HiddenFilter.ExcludeHidden(
                    db.WindowSessions.AsNoTracking()
                        .Where(w => w.OpenTime <= end && (w.CloseTime == null || w.CloseTime >= start)),
                    tagService.GetHiddenRules()),
                tagService.GetIdleRules())
            .OrderBy(w => w.OpenTime)
            .Take(take)
            .Select(w => new WindowSessionRow
            {
                ProcessName = w.ProcessName,
                WindowTitle = w.WindowTitle,
                OpenTime = w.OpenTime,
                CloseTime = w.CloseTime
            })
            .ToListAsync();

        if (!raw)
        {
            var snap = normalizer.CreateSnapshot();
            foreach (var s in sessions)
                s.WindowTitle = snap.Apply(s.ProcessName, s.WindowTitle);
        }

        return Results.Ok(sessions);
    }

    private static async Task<IResult> GetSystemEvents(
        DateTime? from, DateTime? to, AppDbContext db)
    {
        var start = from.HasValue ? NormalizeToUtc(from.Value) : DateTime.UtcNow.AddDays(-7);
        var end = to.HasValue ? NormalizeToUtc(to.Value) : DateTime.UtcNow;

        var eventTypes = new[]
        {
            SystemEventTypes.Sleep, SystemEventTypes.Shutdown, SystemEventTypes.Idle
        };
        var events = await db.SystemEvents
            .AsNoTracking()
            .Where(e => eventTypes.Contains(e.EventType)
                && e.Timestamp >= start && e.Timestamp < end)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        // At most one earlier interval of each type can be the interval crossing
        // the left boundary. This avoids reading every historical SystemEvent.
        foreach (var eventType in eventTypes)
        {
            var preceding = await db.SystemEvents.AsNoTracking()
                .Where(e => e.EventType == eventType && e.Timestamp < start)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefaultAsync();
            if (preceding != null) events.Add(preceding);
        }

        var result = events
            .Where(e => e.Timestamp.AddSeconds(e.DurationSeconds) > start)
            .OrderBy(e => e.Timestamp)
            .Select(e => new
            {
                e.EventType,
                e.Timestamp,
                e.DurationSeconds
            })
            .ToList();

        return Results.Ok(result);
    }

    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
    };
}

internal sealed class TimelineRow
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
}

internal sealed class WindowSessionRow
{
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public DateTime OpenTime { get; set; }
    public DateTime? CloseTime { get; set; }
}
