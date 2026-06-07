using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Service.Api;

public static class WindowEndpoints
{
    public static void MapWindowEndpoints(this WebApplication app)
    {
        app.MapGet("/api/windows/current", GetCurrentWindows);
        app.MapGet("/api/windows/timeline", GetTimeline);
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
        DateTime? from, DateTime? to, int? limit, int? offset, AppDbContext db)
    {
        var start = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow.AddHours(-1);
        var end = to.HasValue
            ? DateTime.SpecifyKind(to.Value, DateTimeKind.Local).ToUniversalTime()
            : DateTime.UtcNow;

        var take = Math.Clamp(limit ?? 500, 1, 2000);
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
                f.DurationSeconds
            })
            .ToListAsync();

        return Results.Ok(new { data, total, offset = skip, limit = take });
    }
}
