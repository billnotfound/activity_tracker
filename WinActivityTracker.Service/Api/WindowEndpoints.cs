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

    private static IResult GetCurrentWindows(SettingsService settings, ProcessNameCache processCache)
    {
        var excluded = settings.Settings.ExcludedProcesses;
        var windows = WindowTracker.EnumerateVisibleWindows(processCache);
        return Results.Ok(windows
            .Where(w => !excluded.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase))
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

        var total = await db.FocusChanges
            .Where(f => f.Timestamp >= start && f.Timestamp <= end)
            .CountAsync();

        var data = await db.FocusChanges
            .Where(f => f.Timestamp >= start && f.Timestamp <= end)
            .OrderBy(f => f.Timestamp)
            .Skip(skip)
            .Take(take)
            .Select(f => new
            {
                f.Timestamp,
                f.ProcessName,
                f.WindowTitle,
                f.DurationSeconds,
                Tags = tagService.ResolveTags(f.ProcessName, f.WindowTitle)
            })
            .ToListAsync();

        return Results.Ok(new { data, total, offset = skip, limit = take });
    }

    private static async Task<IResult> GetWindowSessions(
        DateTime? from, DateTime? to, int? limit, AppDbContext db)
    {
        var start = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow.AddHours(-1);
        var end = to.HasValue
            ? DateTime.SpecifyKind(to.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow;

        var take = Math.Clamp(limit ?? 5000, 1, 50000);

        // Get window sessions that overlap with the time range
        var sessions = await db.WindowSessions
            .AsNoTracking()
            .Where(w => w.OpenTime <= end && (w.CloseTime == null || w.CloseTime >= start))
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
            .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown)
                && e.Timestamp >= start && e.Timestamp <= end)
            .OrderBy(e => e.Timestamp)
            .Select(e => new
            {
                e.EventType,
                e.Timestamp,
                e.DurationSeconds
            })
            .ToListAsync();

        return Results.Ok(events);
    }
}
