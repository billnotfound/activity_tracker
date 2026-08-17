namespace WinActivityTracker.Core.Trackers;

/// <summary>一次时间参照查询结果。OffsetSeconds = 本机墙钟 − 参照时间（+ 本机偏快）。</summary>
public class TimeReferenceResult
{
    public bool Succeeded { get; set; }
    public DateTime ReferenceUtc { get; set; }
    public double OffsetSeconds { get; set; }
    public double LatencyMs { get; set; }
    public string SourceName { get; set; } = string.Empty;
}
