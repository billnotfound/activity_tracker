namespace WinActivityTracker.Service.Api;

internal sealed class IntervalOverlapIndex
{
    private readonly DateTime[] _starts;
    private readonly DateTime[] _prefixMaxEnds;

    public IntervalOverlapIndex(IEnumerable<(DateTime Start, DateTime End)> intervals)
    {
        var ordered = intervals.OrderBy(x => x.Start).ToArray();
        _starts = new DateTime[ordered.Length];
        _prefixMaxEnds = new DateTime[ordered.Length];

        var maxEnd = DateTime.MinValue;
        for (var i = 0; i < ordered.Length; i++)
        {
            _starts[i] = ordered[i].Start;
            if (ordered[i].End > maxEnd) maxEnd = ordered[i].End;
            _prefixMaxEnds[i] = maxEnd;
        }
    }

    public bool Overlaps(DateTime start, DateTime end)
    {
        var low = 0;
        var high = _starts.Length;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (_starts[mid] <= end) low = mid + 1;
            else high = mid;
        }

        var index = low - 1;
        return index >= 0 && _prefixMaxEnds[index] >= start;
    }
}
