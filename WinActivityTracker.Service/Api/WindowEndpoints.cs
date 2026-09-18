using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
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
        app.MapGet("/api/processes/relations", GetProcessRelations);
        app.MapGet("/api/processes/sessions", GetProcessSessions);
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
        DateTime? from, DateTime? to, int? limit, int? offset, string? process,
        AppDbContext db, TagService tagService, TitleNormalizer normalizer, bool raw = false)
    {
        var start = from.HasValue ? NormalizeToUtc(from.Value) : DateTime.UtcNow.AddHours(-1);
        var end = to.HasValue ? NormalizeToUtc(to.Value) : DateTime.UtcNow;

        var take = Math.Clamp(limit ?? 2000, 1, 50000);
        var skip = Math.Max(0, offset ?? 0);

        var baseQuery = HiddenFilter.ExcludeHidden(
            db.FocusChanges.AsNoTracking().Where(f => f.Timestamp >= start && f.Timestamp <= end),
            tagService.GetHiddenRules());

        baseQuery = IdleFilter.ExcludeIdle(baseQuery, tagService.GetIdleRules());

        if (!string.IsNullOrWhiteSpace(process))
        {
            var exactProcess = process.Trim();
            baseQuery = baseQuery.Where(f =>
                EF.Functions.Collate(f.ProcessName, "NOCASE") == exactProcess);
        }

        var total = await baseQuery.CountAsync();

        const int maxPoints = 8000;
        var sampled = false;
        List<TimelineRow> rows;
        if (total > maxPoints && skip == 0)
        {
            var dayAnchorIds = await baseQuery
                .GroupBy(f => f.Timestamp.Date)
                .Select(group => group.Min(f => f.Id))
                .ToListAsync();
            var canAnchorEveryDay = dayAnchorIds.Count <= maxPoints - 2;
            var sampleBudget = canAnchorEveryDay
                ? Math.Max(1, maxPoints - dayAnchorIds.Count - 1)
                : maxPoints - 2;
            var step = (total + sampleBudget - 1) / sampleBudget;
            rows = await baseQuery
                .Where(f => f.Id % step == 0)
                .OrderBy(f => f.Timestamp)
                .Take(sampleBudget)
                .Select(f => new TimelineRow
                {
                    Id = f.Id,
                    Timestamp = f.Timestamp,
                    ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle,
                    DurationSeconds = f.DurationSeconds
                })
                .ToListAsync();

            if (canAnchorEveryDay)
            {
                var anchors = await baseQuery
                    .Where(f => dayAnchorIds.Contains(f.Id))
                    .Select(f => new TimelineRow
                    {
                        Id = f.Id,
                        Timestamp = f.Timestamp,
                        ProcessName = f.ProcessName,
                        WindowTitle = f.WindowTitle,
                        DurationSeconds = f.DurationSeconds
                    })
                    .ToListAsync();
                var sampledIds = rows.Select(row => row.Id).ToHashSet();
                rows.AddRange(anchors.Where(anchor => sampledIds.Add(anchor.Id)));
            }
            else
            {
                var first = await baseQuery.OrderBy(f => f.Timestamp)
                    .Select(f => new TimelineRow
                    {
                        Id = f.Id, Timestamp = f.Timestamp, ProcessName = f.ProcessName,
                        WindowTitle = f.WindowTitle, DurationSeconds = f.DurationSeconds
                    })
                    .FirstAsync();
                if (rows.All(row => row.Id != first.Id)) rows.Add(first);
            }

            var last = await baseQuery.OrderByDescending(f => f.Timestamp)
                .Select(f => new TimelineRow
                {
                    Id = f.Id, Timestamp = f.Timestamp, ProcessName = f.ProcessName,
                    WindowTitle = f.WindowTitle, DurationSeconds = f.DurationSeconds
                })
                .FirstAsync();
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

        var snap = normalizer.CreateSnapshot();
        foreach (var s in sessions)
        {
            if (!raw) s.WindowTitle = snap.Apply(s.ProcessName, s.WindowTitle);
            s.Tags = tagService.ResolveTags(s.ProcessName, s.WindowTitle);
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

    private static IResult GetProcessRelations(string process, ProcessNameCache processCache)
    {
        var snapshot = processCache.Snapshot.ToArray();
        var matches = snapshot
            .Where(x => string.Equals(x.Value.Name, process, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var parents = matches
            .Select(x => x.Value.ParentPid != 0
                ? snapshot.FirstOrDefault(candidate => candidate.Key == x.Value.ParentPid)
                : default)
            .Where(x => x.Value != null)
            .Select(x => new { processId = x.Key, name = x.Value.Name })
            .Distinct()
            .OrderBy(x => x.name)
            .ThenBy(x => x.processId)
            .ToList();

        var processIds = matches.Select(x => x.Key).ToHashSet();
        var instances = matches
            .Select(x => new { processId = x.Key, name = x.Value.Name })
            .OrderBy(x => x.processId)
            .ToList();
        var children = snapshot
            .Where(x => processIds.Contains(x.Value.ParentPid))
            .Select(x => new { processId = x.Key, name = x.Value.Name })
            .Distinct()
            .OrderBy(x => x.name)
            .ToList();

        return Results.Ok(new { instances, parent = parents.FirstOrDefault(), parents, children });
    }

    private static async Task<IResult> GetProcessSessions(
        AppDbContext db, TagService tagService,
        DateTime? from = null, DateTime? to = null, string? ids = null,
        string? process = null, bool tagged = false, int? limit = null)
    {
        var processIds = (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(128)
            .ToList();
        if (processIds.Count == 0 && string.IsNullOrWhiteSpace(process) && !tagged)
            return Results.Ok(Array.Empty<object>());

        var start = from.HasValue ? NormalizeToUtc(from.Value) : DateTime.UtcNow.AddDays(-1);
        var end = to.HasValue ? NormalizeToUtc(to.Value) : DateTime.UtcNow;
        var baseQuery = db.ProcessSessions.AsNoTracking()
            .Where(session => session.StartTime <= end && (session.EndTime == null || session.EndTime >= start));
        if (processIds.Count > 0) baseQuery = baseQuery.Where(session => processIds.Contains(session.ProcessId));
        var query = IdleFilter.ExcludeIdle(
            HiddenFilter.ExcludeHidden(
                baseQuery,
                tagService.GetHiddenRules()),
            tagService.GetIdleRules());

        var take = Math.Clamp(limit ?? 50000, 1, 50000);
        var rows = query.OrderBy(session => session.StartTime)
            .Select(session => new ProcessSessionRow
            {
                ProcessName = session.ProcessName,
                ProcessId = session.ProcessId,
                StartTime = session.StartTime,
                EndTime = session.EndTime
            });

        Regex? matcher = null;
        if (!string.IsNullOrWhiteSpace(process))
        {
            try { matcher = new Regex(process, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException)
            {
                matcher = new Regex(Regex.Escape(process), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
        }

        var result = new List<ProcessSessionResponse>(take);
        await foreach (var row in rows.AsAsyncEnumerable())
        {
            if (matcher != null)
            {
                try
                {
                    if (!matcher.IsMatch(row.ProcessName)) continue;
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }
            }

            var tags = tagService.ResolveTags(row.ProcessName, null);
            if (tagged && tags.Count == 0) continue;
            result.Add(new ProcessSessionResponse
            {
                ProcessName = row.ProcessName,
                ProcessId = row.ProcessId,
                StartTime = row.StartTime,
                EndTime = row.EndTime,
                Tags = tags
            });
            if (result.Count == take) break;
        }
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
    public List<string> Tags { get; set; } = [];
}

internal class ProcessSessionRow
{
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

internal sealed class ProcessSessionResponse : ProcessSessionRow
{
    public List<string> Tags { get; set; } = [];
}
