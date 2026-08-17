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
        int? limit, string? from, string? to, AppDbContext db, TagService tagService)
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

        query = HiddenFilter.ExcludeHidden(query, tagService.GetHiddenRules());

        var fetchCount = (limit ?? 50) * 3;

        var data = await query
            .OrderByDescending(m => m.StartTime)
            .Take(fetchCount)
            .ToListAsync();
        data.Reverse();

        var merged = MergeConsecutive(data);

        // Idle/hidden-tagged processes hide their media records too. Matched
        // in-memory: records are already merged and the rule count is small.
        merged = await FilterIdleMedia(db, merged, tagService.GetIdleRules(), tagService.GetHiddenRules());

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

    /// <summary>
    /// Hides media records under idle/hidden tags:
    /// - a record whose own process/title matches a __hidden rule is always
    ///   hidden (the SQL layer above already does this for raw rows)
    /// - a record matching a strong idle rule (weight >= 10) is hidden
    /// - a record matching a weak idle rule (weight &lt; 10) is hidden unless the
    ///   process has foreground focus during the record's span (weak rules are
    ///   overridden by foreground activity, e.g. actively playing music)
    /// - any focus record inside the record's span that is hidden or strong-idle
    ///   hides the record too (lock screen, screensaver, ... hide all media of
    ///   that period regardless of producing process)
    /// </summary>
    private static async Task<List<MediaSessionRecord>> FilterIdleMedia(
        AppDbContext db,
        List<MediaSessionRecord> merged,
        List<TagService.TagRule> idleRules,
        List<TagService.TagRule> hiddenRules)
    {
        if (merged.Count == 0) return merged;

        var strongIdle = idleRules.Where(r => r.Weight >= 10).ToList();
        var weakIdle = idleRules.Where(r => r.Weight < 10).ToList();
        if (strongIdle.Count == 0 && weakIdle.Count == 0) return merged;

        var hideIds = new HashSet<long>();
        var weakCandidates = new List<MediaSessionRecord>();

        foreach (var m in merged)
        {
            if (TagService.MatchesIdle(strongIdle, m.AppName, m.Title))
                hideIds.Add(m.Id);
            else if (TagService.MatchesIdle(weakIdle, m.AppName, m.Title))
                weakCandidates.Add(m);
        }

        var needFocus = weakCandidates.Count > 0
            || (merged.Any(m => !hideIds.Contains(m.Id)) && (strongIdle.Count > 0 || hiddenRules.Count > 0));

        if (needFocus)
        {
            var minStart = merged.Min(m => m.StartTime);
            var maxEnd = merged.Max(m => m.EndTime ?? DateTime.UtcNow);

            // One day of slack before minStart so a focus record that started
            // earlier can still overlap the earliest media record.
            var focusRows = await db.FocusChanges.AsNoTracking()
                .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess)
                .Where(f => f.Timestamp <= maxEnd && f.Timestamp >= minStart.AddDays(-1))
                .Select(f => new { f.ProcessName, f.Timestamp, f.DurationSeconds, f.WindowTitle })
                .ToListAsync();

            // Focus span that is hidden or strong-idle hides the media record.
            if (strongIdle.Count > 0 || hiddenRules.Count > 0)
            {
                foreach (var m in merged)
                {
                    if (hideIds.Contains(m.Id)) continue;
                    var start = m.StartTime;
                    var end = m.EndTime ?? DateTime.UtcNow;
                    foreach (var f in focusRows)
                    {
                        if (f.Timestamp > end || f.Timestamp.AddSeconds(f.DurationSeconds) < start) continue;
                        if (TagService.MatchesHidden(hiddenRules, f.ProcessName, f.WindowTitle)
                            || TagService.MatchesIdle(strongIdle, f.ProcessName, f.WindowTitle))
                        {
                            hideIds.Add(m.Id);
                            break;
                        }
                    }
                }
            }

            // Weak idle rules are overridden by foreground focus on the process.
            foreach (var m in weakCandidates)
            {
                if (hideIds.Contains(m.Id)) continue;
                var start = m.StartTime;
                var end = m.EndTime ?? DateTime.UtcNow;
                var hasFocus = focusRows.Any(f =>
                    string.Equals(f.ProcessName, m.AppName, StringComparison.OrdinalIgnoreCase)
                    && f.Timestamp <= end && f.Timestamp.AddSeconds(f.DurationSeconds) >= start);
                if (!hasFocus) hideIds.Add(m.Id);
            }
        }

        return merged.Where(m => !hideIds.Contains(m.Id)).ToList();
    }

    private static List<MediaSessionRecord> MergeConsecutive(List<MediaSessionRecord> records)
    {
        if (records.Count == 0) return records;

        var result = new List<MediaSessionRecord>();
        MediaSessionRecord? group = null;

        foreach (var r in records)
        {
            if (group != null
                && group.AppName == r.AppName
                && group.Title == r.Title
                && group.Artist == r.Artist
                && group.PlaybackStatus == r.PlaybackStatus)
            {
                group.EndTime = r.EndTime;
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

    private static async Task<IResult> GetProcessSnapshot(AppDbContext db, TagService tagService)
    {
        var data = await IdleFilter.ExcludeIdle(
                HiddenFilter.ExcludeHidden(
                    db.ProcessSessions.Where(p => p.EndTime == null),
                    tagService.GetHiddenRules()),
                tagService.GetIdleRules())
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

        // Keyset pagination (WHERE Id > lastId) instead of OFFSET: each batch
        // scans only forward from the previous cursor, avoiding O(n²) rescans.
        long? lastId = null;
        while (true)
        {
            var query = db.FocusChanges
                .Where(f => f.WindowTitle != "")
                .OrderBy(f => f.Id)
                .Take(batchSize);
            if (lastId.HasValue)
                query = query.Where(f => f.Id > lastId.Value);

            var batch = await query.ToListAsync();
            if (batch.Count == 0)
                break;

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

            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync();
            // Clear the tracker so entities don't accumulate in memory across batches.
            db.ChangeTracker.Clear();
            lastId = batch[^1].Id;
        }

        return Results.Ok(new { totalRows, modifiedRows });
    }
}
