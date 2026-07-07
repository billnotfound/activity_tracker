using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class MediaEndpoints
{
    private static readonly JsonSerializerOptions _saveOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void MapMediaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/media/history", GetMediaHistory);
        app.MapGet("/api/processes/snapshot", GetProcessSnapshot);
        app.MapGet("/api/title-rules", GetTitleRules);
        app.MapPut("/api/title-rules/save", SaveTitleRules);
        app.MapPost("/api/title-rules/normalize-db", NormalizeDb);
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

        var fetchCount = (limit ?? 50) * 3;

        var data = await query
            .OrderByDescending(m => m.StartTime)
            .Take(fetchCount)
            .ToListAsync();
        data.Reverse();

        var merged = MergeConsecutive(data);

        if (merged.Count > (limit ?? 50))
            merged = merged.GetRange(merged.Count - (limit ?? 50), limit ?? 50);

        return Results.Ok(merged.Select(m => new
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

    private static List<MediaSessionRecord> MergeConsecutive(List<MediaSessionRecord> records)
    {
        if (records.Count == 0) return records;

        var statusPriority = new Dictionary<string, int>
        {
            ["Playing"] = 5, ["Paused"] = 4, ["Stopped"] = 3,
            ["Opened"] = 2, ["Changing"] = 1, ["Closed"] = 0
        };

        var result = new List<MediaSessionRecord>();
        MediaSessionRecord? group = null;

        foreach (var r in records)
        {
            if (group != null
                && group.AppName == r.AppName
                && group.Title == r.Title
                && group.Artist == r.Artist)
            {
                group.EndTime = r.EndTime;
                if (statusPriority.GetValueOrDefault(r.PlaybackStatus, 0)
                    > statusPriority.GetValueOrDefault(group.PlaybackStatus, 0))
                    group.PlaybackStatus = r.PlaybackStatus;
            }
            else
            {
                group = new MediaSessionRecord
                {
                    Id = r.Id,
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    AppName = r.AppName,
                    Title = r.Title,
                    Artist = r.Artist,
                    PlaybackStatus = r.PlaybackStatus
                };
                result.Add(group);
            }
        }

        return result;
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

    private static async Task<IResult> SaveTitleRules(HttpRequest request, TitleNormalizer normalizer, AppPaths appPaths)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var rules = JsonSerializer.Deserialize<List<TitleNormalizer.TitleRule>>(body, _readOptions);

            if (rules == null || rules.Count == 0)
                return Results.BadRequest(new { error = I18nService._("error.emptyRuleList") });

            var path = Path.Combine(appPaths.ConfigDir, "title_rules.json");
            var json = JsonSerializer.Serialize(rules, _saveOptions);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, path, overwrite: true);

            return Results.Ok(new { saved = rules.Count });
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = I18nService._("tags.jsonError", ex.Message) });
        }
        catch (Exception ex)
        {
            return Results.Problem(I18nService._("error.saveFailed", ex.Message));
        }
    }

    private static async Task<IResult> NormalizeDb(AppDbContext db, TitleNormalizer normalizer)
    {
        const int batchSize = 500;
        var totalRows = 0;
        var modifiedRows = 0;

        var total = await db.FocusChanges.CountAsync(f => f.WindowTitle != "");
        if (total == 0)
            return Results.Ok(new { totalRows = 0, modifiedRows = 0 });

        for (var offset = 0; offset < total; offset += batchSize)
        {
            var batch = await db.FocusChanges
                .Where(f => f.WindowTitle != "")
                .OrderBy(f => f.Id)
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync();

            foreach (var row in batch)
            {
                totalRows++;
                var normalized = normalizer.Apply(row.ProcessName, row.WindowTitle);
                if (normalized != row.WindowTitle)
                {
                    row.WindowTitle = normalized;
                    modifiedRows++;
                }
            }

            if (batch.Count > 0)
            {
                db.ChangeTracker.DetectChanges();
                await db.SaveChangesAsync();
            }
        }

        return Results.Ok(new { totalRows, modifiedRows });
    }
}
