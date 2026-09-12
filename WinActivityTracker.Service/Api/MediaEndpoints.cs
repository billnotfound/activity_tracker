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
        var query = db.MediaSessionRecords.AsNoTracking();

        if (from != null && DateOnly.TryParse(from, out var fromDate))
        {
            var start = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
            // Include sessions that began before the selected day but overlap it.
            // Damaged legacy rows are retained when their StartTime is in range.
            query = query.Where(m => m.StartTime >= start
                || m.EndTime == null || m.EndTime >= start);
        }
        if (to != null && DateOnly.TryParse(to, out var toDate))
        {
            var end = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local).ToUniversalTime();
            query = query.Where(m => m.StartTime <= end);
        }

        query = HiddenFilter.ExcludeHidden(query, tagService.GetHiddenRules());

        var take = Math.Clamp(limit ?? 50, 1, 1000);
        var fetchCount = take * 3;

        var data = await query
            .OrderByDescending(m => m.StartTime)
            .Take(fetchCount)
            .ToListAsync();
        data.Reverse();

        // Idle/hidden-tagged processes hide their media records too. Matched
        // in-memory while source ids and intervals are still exact.
        data = await FilterIdleMedia(db, data, tagService.GetIdleRules(), tagService.GetHiddenRules());

        // Preserve every DB row but return a compact display projection. Equal
        // media/status rows separated only by SystemSleep markers are one group;
        // anomaly/source counts let the UI label damaged legacy data explicitly.
        var merged = MergeConsecutive(data);

        if (merged.Count > take)
            merged = merged.GetRange(merged.Count - take, take);

        return Results.Ok(merged);
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
            var maxEnd = merged.Max(EffectiveEnd);

            var focusRows = await db.FocusChanges.AsNoTracking()
                .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess)
                .Where(f => f.Timestamp >= minStart && f.Timestamp <= maxEnd)
                .Select(f => new { f.ProcessName, f.Timestamp, f.DurationSeconds, f.WindowTitle })
                .ToListAsync();
            var preceding = await db.FocusChanges.AsNoTracking()
                .Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess && f.Timestamp < minStart)
                .OrderByDescending(f => f.Timestamp)
                .Select(f => new { f.ProcessName, f.Timestamp, f.DurationSeconds, f.WindowTitle })
                .FirstOrDefaultAsync();
            if (preceding != null) focusRows.Insert(0, preceding);

            var globalHidden = new List<(DateTime Start, DateTime End)>();
            var focusByProcess = new Dictionary<string, List<(DateTime Start, DateTime End)>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var f in focusRows)
            {
                var interval = (f.Timestamp,
                    f.Timestamp.AddSeconds(Math.Max(0, f.DurationSeconds)));
                if (!focusByProcess.TryGetValue(f.ProcessName, out var processIntervals))
                {
                    processIntervals = [];
                    focusByProcess[f.ProcessName] = processIntervals;
                }
                processIntervals.Add(interval);

                if (TagService.MatchesHidden(hiddenRules, f.ProcessName, f.WindowTitle)
                    || TagService.MatchesIdle(strongIdle, f.ProcessName, f.WindowTitle))
                    globalHidden.Add(interval);
            }

            var globalHiddenIndex = new IntervalOverlapIndex(globalHidden);
            var processIndexes = focusByProcess.ToDictionary(
                x => x.Key, x => new IntervalOverlapIndex(x.Value), StringComparer.OrdinalIgnoreCase);

            // Focus span that is hidden or strong-idle hides the media record.
            if (strongIdle.Count > 0 || hiddenRules.Count > 0)
            {
                foreach (var m in merged)
                {
                    if (hideIds.Contains(m.Id)) continue;
                    var start = m.StartTime;
                    var end = EffectiveEnd(m);
                    if (globalHiddenIndex.Overlaps(start, end)) hideIds.Add(m.Id);
                }
            }

            // Weak idle rules are overridden by foreground focus on the process.
            foreach (var m in weakCandidates)
            {
                if (hideIds.Contains(m.Id)) continue;
                var start = m.StartTime;
                var end = EffectiveEnd(m);
                var hasFocus = processIndexes.TryGetValue(m.AppName, out var processIndex)
                    && processIndex.Overlaps(start, end);
                if (!hasFocus) hideIds.Add(m.Id);
            }
        }

        return merged.Where(m => !hideIds.Contains(m.Id)).ToList();
    }

    internal static List<MediaHistoryGroup> MergeConsecutive(List<MediaSessionRecord> records)
    {
        if (records.Count == 0) return [];

        var result = new List<MediaHistoryGroup>();
        var pendingMarkers = new List<MediaSessionRecord>();
        MediaHistoryGroup? group = null;

        foreach (var r in records)
        {
            if (IsSystemMarker(r))
            {
                pendingMarkers.Add(r);
                continue;
            }

            if (group != null && group.Matches(r))
            {
                group.Add(r);
                group.ConsumeMarkers(pendingMarkers);
                pendingMarkers.Clear();
            }
            else
            {
                if (group != null) result.Add(group);
                AppendMarkers(result, pendingMarkers);
                pendingMarkers.Clear();
                group = MediaHistoryGroup.From(r);
            }
        }

        if (group != null) result.Add(group);
        AppendMarkers(result, pendingMarkers);
        return result;
    }

    private static void AppendMarkers(
        List<MediaHistoryGroup> result, IEnumerable<MediaSessionRecord> markers)
    {
        foreach (var marker in markers)
        {
            if (result.Count > 0 && result[^1].Matches(marker))
                result[^1].Add(marker);
            else
                result.Add(MediaHistoryGroup.From(marker));
        }
    }

    private static bool IsSystemMarker(MediaSessionRecord record) =>
        record.PlaybackStatus == SystemMarkers.SystemSleepStatus
        || record.AppName == SystemMarkers.SystemSleepProcess;

    private static DateTime EffectiveEnd(MediaSessionRecord record)
    {
        var end = record.EndTime ?? DateTime.UtcNow;
        return end < record.StartTime ? record.StartTime : end;
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

internal sealed class MediaHistoryGroup
{
    public long Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string PlaybackStatus { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int AnomalousRecordCount { get; set; }
    public int SystemMarkerCount { get; set; }
    public bool IsAnomalous => AnomalousRecordCount > 0;

    [JsonIgnore]
    public bool HasOpenRecord { get; set; }

    public static MediaHistoryGroup From(MediaSessionRecord record)
    {
        var group = new MediaHistoryGroup
        {
            Id = record.Id,
            StartTime = record.StartTime,
            AppName = record.AppName,
            Title = record.Title,
            Artist = record.Artist,
            PlaybackStatus = record.PlaybackStatus
        };
        group.Add(record);
        return group;
    }

    public bool Matches(MediaSessionRecord record) =>
        AppName == record.AppName
        && Title == record.Title
        && Artist == record.Artist
        && PlaybackStatus == record.PlaybackStatus;

    public void Add(MediaSessionRecord record)
    {
        if (RecordCount > 0 && record.StartTime < StartTime)
            StartTime = record.StartTime;

        RecordCount++;
        if (record.EndTime.HasValue && record.EndTime.Value < record.StartTime)
            AnomalousRecordCount++;

        if (!record.EndTime.HasValue)
        {
            HasOpenRecord = true;
            EndTime = null;
            return;
        }

        if (!HasOpenRecord)
        {
            var safeEnd = record.EndTime.Value < record.StartTime
                ? record.StartTime
                : record.EndTime.Value;
            if (!EndTime.HasValue || safeEnd > EndTime.Value)
                EndTime = safeEnd;
        }
    }

    public void ConsumeMarkers(IEnumerable<MediaSessionRecord> markers)
    {
        foreach (var marker in markers)
        {
            SystemMarkerCount++;
            if (HasOpenRecord) continue;
            var markerEnd = marker.EndTime.HasValue && marker.EndTime.Value > marker.StartTime
                ? marker.EndTime.Value
                : marker.StartTime;
            if (!EndTime.HasValue || markerEnd > EndTime.Value)
                EndTime = markerEnd;
        }
    }
}
