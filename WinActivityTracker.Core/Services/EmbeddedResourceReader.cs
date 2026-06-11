using System.Reflection;

namespace WinActivityTracker.Core.Services;

internal static class EmbeddedResourceReader
{
    private static readonly string BaseDir = ResolveBaseDir();

    private static string ResolveBaseDir()
    {
        var loc = typeof(EmbeddedResourceReader).Assembly.Location;
        if (!string.IsNullOrEmpty(loc))
            return Path.GetDirectoryName(loc)!;

        return AppContext.BaseDirectory;
    }

    public static string ReadAsString(string resourceName)
    {
        var path = Path.Combine(BaseDir, "Resources", resourceName);
        return File.ReadAllText(path);
    }
}
