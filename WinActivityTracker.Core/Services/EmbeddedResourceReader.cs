using System.Reflection;

namespace WinActivityTracker.Core.Services;

// NOTE: Despite the name, this reads from disk (Resources/ dir next to the DLL),
// not from embedded assembly resources (Assembly.GetManifestResourceStream).
// The Resources/ directory must be deployed alongside the assembly.
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
