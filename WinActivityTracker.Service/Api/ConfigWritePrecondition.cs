using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

internal static class ConfigWritePrecondition
{
    public const string HeaderName = "X-WTA-Last-Write";

    public static string? GetLastWrite(string path) => File.Exists(path)
        ? File.GetLastWriteTimeUtc(path).ToString("o")
        : null;

    public static IResult? Validate(HttpRequest request, string path)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var values)) return null;
        var expected = values.ToString();
        if (string.IsNullOrWhiteSpace(expected)) return null;

        var actual = GetLastWrite(path);
        return string.Equals(expected, actual, StringComparison.Ordinal)
            ? null
            : Results.Conflict(new
            {
                error = I18nService._("error.configWriteConflict"),
                lastWrite = actual
            });
    }
}
