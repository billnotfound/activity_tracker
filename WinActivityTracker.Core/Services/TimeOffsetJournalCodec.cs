using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;

namespace WinActivityTracker.Core.Services;

public readonly record struct JournalIdRange(long First, long Last)
{
    public long Count => checked(Last - First + 1);
}

public sealed record JournalCompactionResult(int CompactedRows, long BytesSaved);

/// <summary>
/// Stores exact row-id sets as consecutive ranges. Activity-table ids are mostly
/// contiguous, so this turns multi-megabyte JSON arrays into a few kilobytes
/// without weakening restore semantics. Decode also accepts the original
/// Dictionary&lt;string,List&lt;long&gt;&gt; format.
/// </summary>
public static class TimeOffsetJournalCodec
{
    private const int CurrentVersion = 2;

    private sealed class CompactDocument
    {
        [JsonPropertyName("v")]
        public int Version { get; set; } = CurrentVersion;

        [JsonPropertyName("r")]
        public Dictionary<string, List<long[]>> Ranges { get; set; } = new();
    }

    public static List<JournalIdRange> CompressIds(IEnumerable<long> ids)
    {
        var sorted = ids.Distinct().OrderBy(x => x);
        var ranges = new List<JournalIdRange>();
        foreach (var id in sorted)
        {
            if (ranges.Count > 0 && id == ranges[^1].Last + 1)
                ranges[^1] = ranges[^1] with { Last = id };
            else
                ranges.Add(new JournalIdRange(id, id));
        }
        return ranges;
    }

    public static string Encode(IReadOnlyDictionary<string, List<JournalIdRange>> ranges)
    {
        var document = new CompactDocument
        {
            Ranges = ranges.ToDictionary(
                pair => pair.Key,
                pair => NormalizeRanges(pair.Value)
                    .Select(range => new[] { range.First, range.Last })
                    .ToList(),
                StringComparer.Ordinal)
        };
        return JsonSerializer.Serialize(document);
    }

    public static Dictionary<string, List<JournalIdRange>> Decode(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        if (root.TryGetProperty("v", out var version)
            && version.GetInt32() == CurrentVersion
            && root.TryGetProperty("r", out var compactRanges))
        {
            var result = new Dictionary<string, List<JournalIdRange>>(StringComparer.Ordinal);
            foreach (var property in compactRanges.EnumerateObject())
            {
                var ranges = new List<JournalIdRange>();
                foreach (var pair in property.Value.EnumerateArray())
                {
                    if (pair.GetArrayLength() != 2)
                        throw new JsonException("Invalid time-offset journal range.");
                    var first = pair[0].GetInt64();
                    var last = pair[1].GetInt64();
                    if (first <= 0 || last < first)
                        throw new JsonException("Invalid time-offset journal range bounds.");
                    ranges.Add(new JournalIdRange(first, last));
                }
                result[property.Name] = NormalizeRanges(ranges);
            }
            return result;
        }

        var legacy = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(json)
            ?? throw new JsonException("Invalid legacy time-offset journal.");
        return legacy.ToDictionary(
            pair => pair.Key,
            pair => CompressIds(pair.Value),
            StringComparer.Ordinal);
    }

    public static List<JournalIdRange> NormalizeRanges(IEnumerable<JournalIdRange> ranges)
    {
        var sorted = ranges.OrderBy(x => x.First).ThenBy(x => x.Last).ToList();
        var merged = new List<JournalIdRange>(sorted.Count);
        foreach (var range in sorted)
        {
            if (range.First <= 0 || range.Last < range.First)
                throw new JsonException("Invalid time-offset journal range bounds.");
            if (merged.Count > 0 && range.First <= merged[^1].Last + 1)
                merged[^1] = merged[^1] with { Last = Math.Max(merged[^1].Last, range.Last) };
            else
                merged.Add(range);
        }
        return merged;
    }

    /// <summary>
    /// Rewrites legacy journals one row at a time to bound peak memory. Every
    /// update is independently committed, so an interrupted upgrade resumes on
    /// the next launch. Invalid rows are retained for manual recovery.
    /// </summary>
    public static async Task<JournalCompactionResult> CompactLegacyAsync(AppDbContext db)
    {
        var compacted = 0;
        long bytesSaved = 0;
        long cursor = 0;

        while (true)
        {
            var row = await db.TimeOffsetApplications.AsNoTracking()
                .Where(x => x.Id > cursor && !x.RowsJson.StartsWith("{\"v\":2,"))
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.RowsJson })
                .FirstOrDefaultAsync();
            if (row == null) break;
            cursor = row.Id;

            try
            {
                var replacement = Encode(Decode(row.RowsJson));
                if (replacement.Length >= row.RowsJson.Length) continue;
                var affected = await db.TimeOffsetApplications
                    .Where(x => x.Id == row.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RowsJson, replacement));
                if (affected == 1)
                {
                    compacted++;
                    bytesSaved += row.RowsJson.Length - replacement.Length;
                }
            }
            catch (JsonException)
            {
                // Preserve malformed legacy journals; restore will report them
                // as unusable exactly as it did before this migration.
            }
        }

        return new JournalCompactionResult(compacted, bytesSaved);
    }
}
