namespace WinActivityTracker.Service.Api;

internal readonly record struct TimeInterval(DateTime Start, DateTime End)
{
    public double DurationSeconds => Math.Max(0, (End - Start).TotalSeconds);
}

internal static class IntervalMath
{
    public static DateTime SafeAddSeconds(DateTime start, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return start;
        try { return start.AddSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return DateTime.MaxValue; }
    }

    public static List<TimeInterval> Merge(
        IEnumerable<TimeInterval> intervals, DateTime? clipStart = null, DateTime? clipEnd = null)
    {
        var sorted = intervals
            .Select(interval => new TimeInterval(
                clipStart.HasValue && interval.Start < clipStart.Value ? clipStart.Value : interval.Start,
                clipEnd.HasValue && interval.End > clipEnd.Value ? clipEnd.Value : interval.End))
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ThenBy(interval => interval.End)
            .ToList();
        var merged = new List<TimeInterval>(sorted.Count);
        foreach (var interval in sorted)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
                merged[^1] = merged[^1] with { End = Max(merged[^1].End, interval.End) };
            else
                merged.Add(interval);
        }
        return merged;
    }

    public static List<TimeInterval> Subtract(
        DateTime start, DateTime end, IReadOnlyList<TimeInterval> exclusions)
    {
        var result = new List<TimeInterval>();
        if (end <= start) return result;
        var cursor = start;
        foreach (var exclusion in exclusions)
        {
            if (exclusion.End <= cursor) continue;
            if (exclusion.Start >= end) break;
            if (exclusion.Start > cursor)
                result.Add(new TimeInterval(cursor, Min(exclusion.Start, end)));
            if (exclusion.End > cursor) cursor = exclusion.End;
            if (cursor >= end) break;
        }
        if (cursor < end) result.Add(new TimeInterval(cursor, end));
        return result;
    }

    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
}
