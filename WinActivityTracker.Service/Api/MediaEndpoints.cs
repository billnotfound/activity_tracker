using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/media/history", GetMediaHistory);
        app.MapGet("/api/processes/snapshot", GetProcessSnapshot);
        app.MapGet("/api/title-rules", GetTitleRules);
    }

    private static async Task<IResult> GetMediaHistory(
        int? limit, string? from, string? to, AppDbContext db)
    {
        var query = db.MediaSessionRecords.AsQueryable();

        if (from != null && DateOnly.TryParse(from, out var fromDate))
        {
            var start = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
            query = query.Where(m => m.StartTime >= start);
        }
        if (to != null && DateOnly.TryParse(to, out var toDate))
        {
            var end = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local).ToUniversalTime();
            query = query.Where(m => m.StartTime <= end);
        }

        var data = await query
            .OrderByDescending(m => m.StartTime)
            .Take(limit ?? 50)
            .ToListAsync();
        data.Reverse();

        return Results.Ok(data.Select(m => new
        {
            m.Id,
            m.StartTime,
            m.EndTime,
            m.AppName,
            m.Title,
            m.Artist,
            m.PlaybackStatus
        }));
    }

    private static async Task<IResult> GetProcessSnapshot(AppDbContext db)
    {
        var data = await db.ProcessSessions
            .Where(p => p.EndTime == null)
            .Select(p => new { p.ProcessName, p.ProcessId })
            .Distinct()
            .ToListAsync();

        return Results.Ok(data);
    }

    private static IResult GetTitleRules(TitleNormalizer normalizer)
    {
        return Results.Ok(normalizer.GetRules());
    }
}
