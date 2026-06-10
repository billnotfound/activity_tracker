using Microsoft.Win32;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// Resolves config and data directory paths from registry, with fallback to
/// %LOCALAPPDATA%\WinActivityTracker. Handles one-time file migration when
/// paths change.
/// </summary>
public class AppPaths
{
    private const string RegKey = @"Software\WinActivityTracker";
    private const string ConfigDirValue = "ConfigDir";
    private const string DataDirValue = "DataDir";

    private readonly string _defaultBase;

    public string ConfigDir { get; }
    public string DataDir { get; }

    public AppPaths()
    {
        _defaultBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinActivityTracker");

        ConfigDir = ReadRegistry(ConfigDirValue) ?? _defaultBase;
        DataDir = ReadRegistry(DataDirValue) ?? _defaultBase;

        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(DataDir);
    }

    public void MigrateIfNeeded()
    {
        if (!string.Equals(ConfigDir.TrimEnd('\\'), _defaultBase.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            MigrateDirectory(_defaultBase, ConfigDir, "*.json");

        if (!string.Equals(DataDir.TrimEnd('\\'), _defaultBase.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(DataDir.TrimEnd('\\'), ConfigDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            MigrateDirectory(_defaultBase, DataDir, "*.db");
    }

    public static void WriteRegistry(string? configDir, string? dataDir)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegKey);
        if (configDir != null)
            key.SetValue(ConfigDirValue, configDir);
        else
            key.DeleteValue(ConfigDirValue, throwOnMissingValue: false);

        if (dataDir != null)
            key.SetValue(DataDirValue, dataDir);
        else
            key.DeleteValue(DataDirValue, throwOnMissingValue: false);
    }

    public static (string? ConfigDir, string? DataDir) ReadRegistryValues()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey);
        return (
            key?.GetValue(ConfigDirValue) as string,
            key?.GetValue(DataDirValue) as string
        );
    }

    private static string? ReadRegistry(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey);
        var val = key?.GetValue(valueName) as string;
        if (string.IsNullOrWhiteSpace(val)) return null;
        return val;
    }

    private static void MigrateDirectory(string fromDir, string toDir, string pattern)
    {
        if (!Directory.Exists(fromDir)) return;

        foreach (var file in Directory.GetFiles(fromDir, pattern))
        {
            var dest = Path.Combine(toDir, Path.GetFileName(file));
            if (!File.Exists(dest))
                File.Move(file, dest);
        }

        // Clean up empty old directory (leave non-empty ones alone)
        try
        {
            if (Directory.GetFiles(fromDir).Length == 0
                && Directory.GetDirectories(fromDir).Length == 0)
                Directory.Delete(fromDir);
        }
        catch { /* not critical */ }
    }
}
