namespace WinActivityTracker.Updater;

/// <summary>
/// Reads and compares resource.txt manifests (program file inventory).
/// Embedded resource in the updater assembly; also read from disk for the old version.
/// </summary>
internal class ResourceManifest
{
    public IReadOnlySet<string> Entries { get; }
    public IReadOnlySet<string> FileEntries { get; }
    public IReadOnlySet<string> DirectoryEntries { get; }

    private ResourceManifest(HashSet<string> entries)
    {
        Entries = entries;
        FileEntries = entries.Where(e => !e.EndsWith('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        DirectoryEntries = entries.Where(e => e.EndsWith('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Load the embedded resource manifest from the running assembly.
    /// </summary>
    public static ResourceManifest LoadEmbedded()
    {
        return LoadEmbeddedByName("resource.txt");
    }

    /// <summary>
    /// Load the embedded baseline old manifest (old.resource.txt).
    /// Represents the file structure of the version before the updater existed.
    /// Used as fallback when the old install has no resource.txt.
    /// </summary>
    public static ResourceManifest? LoadEmbeddedOld()
    {
        try
        {
            return LoadEmbeddedByName("old.resource.txt");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static ResourceManifest LoadEmbeddedByName(string resourceNameSuffix)
    {
        var assembly = typeof(ResourceManifest).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        var resourceName = resourceNames.FirstOrDefault(n =>
            n.EndsWith(resourceNameSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceNameSuffix}' not found in assembly.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Load a resource.txt from disk. Returns null if the file doesn't exist.
    /// </summary>
    public static ResourceManifest? LoadFromDisk(string directoryPath)
    {
        var path = Path.Combine(directoryPath, "resource.txt");
        if (!File.Exists(path)) return null;
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Parse resource.txt: one entry per line. '#' and ';' are comments. Empty lines ignored.
    /// Directory entries end with '/'.
    /// </summary>
    public static ResourceManifest Parse(string text)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed[0] is '#' or ';') continue;
            entries.Add(trimmed.Replace('\\', '/'));
        }
        return new ResourceManifest(entries);
    }

    /// <summary>
    /// Files that exist in 'oldManifest' but NOT in this manifest (new version).
    /// Directory entries are excluded.
    /// </summary>
    public List<string> GetRemovedFiles(ResourceManifest oldManifest)
    {
        return oldManifest.FileEntries
            .Where(e => !FileEntries.Contains(e))
            .ToList();
    }
}
