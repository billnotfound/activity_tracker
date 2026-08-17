using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Core.Services;

/// <summary>HTTPS GET 任意 URL，读响应 Date 头（UTC）作为标准时间。</summary>
public class HttpTimeReference : ITimeReference
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string _url;

    public HttpTimeReference(string url)
        => _url = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url : $"https://{url}";

    public async Task<TimeReferenceResult> QueryAsync(CancellationToken ct)
    {
        var t0 = DateTime.UtcNow;
        try
        {
            using var resp = await _client.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, ct);
            var t1 = DateTime.UtcNow;
            if (!resp.Headers.Date.HasValue)
                return new TimeReferenceResult { Succeeded = false, ReferenceUtc = t1, SourceName = $"http:{_url} (no Date header)" };
            var reference = resp.Headers.Date.Value.UtcDateTime;
            var latency = (t1 - t0).TotalSeconds;
            var localWall = DateTime.UtcNow;
            return new TimeReferenceResult
            {
                Succeeded = true,
                ReferenceUtc = reference,
                OffsetSeconds = (localWall - reference).TotalSeconds,
                LatencyMs = latency * 1000,
                SourceName = $"http:{_url}"
            };
        }
        catch (Exception ex)
        {
            // 网络失败：吞掉异常，返回 Succeeded=false
            return new TimeReferenceResult { Succeeded = false, ReferenceUtc = DateTime.UtcNow, SourceName = $"http:{_url} ({ex.Message})" };
        }
    }

    /// <summary>解析 HTTP Date 头（RFC 1123 "Thu, 14 Aug 2026 08:00:00 GMT" 等）。失败返回 null。纯静态方法，供单元测试。</summary>
    public static DateTime? ParseDateHeader(string header)
    {
        if (DateTime.TryParse(header, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var t))
            return t;
        return null;
    }
}
