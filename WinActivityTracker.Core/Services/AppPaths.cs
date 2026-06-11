using Microsoft.Win32;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// Resolves config and data directory paths from registry, with fallback to
/// %LOCALAPPDATA%\WinActivityTracker. Handles one-time file migration when
/// paths change.
/// </summary>
public class AppPaths
{
    private static readonly string RegKey =
        Environment.GetEnvironmentVariable("WTA_REG_KEY")
        ?? @"Software\WinActivityTracker";

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

        var regConfig = ReadRegistry(ConfigDirValue);
        var regData = ReadRegistry(DataDirValue);

        ConfigDir = EnsureDirectory(regConfig ?? _defaultBase, _defaultBase);
        DataDir = EnsureDirectory(regData ?? _defaultBase, _defaultBase);

        // If a registry path was invalid and we fell back, clear the bad entry.
        if (regConfig != null && !string.Equals(ConfigDir.TrimEnd('\\'), regConfig.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            ClearRegistryValue(ConfigDirValue);
        if (regData != null && !string.Equals(DataDir.TrimEnd('\\'), regData.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            ClearRegistryValue(DataDirValue);
    }

    public void MigrateIfNeeded()
    {
        if (!string.Equals(ConfigDir.TrimEnd('\\'), _defaultBase.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(I18nService._("appPaths.configDirChanged", _defaultBase, ConfigDir));
            MigrateDirectory(_defaultBase, ConfigDir, "*.json");
        }

        if (!string.Equals(DataDir.TrimEnd('\\'), _defaultBase.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(DataDir.TrimEnd('\\'), ConfigDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(I18nService._("appPaths.dataDirChanged", _defaultBase, DataDir));
            MigrateDirectory(_defaultBase, DataDir, "*.db");
        }
    }

    private static string EnsureDirectory(string path, string fallback)
    {
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(I18nService._("appPaths.cannotCreateDir", path, ex.Message, fallback));
            try { Directory.CreateDirectory(fallback); } catch { }
            return fallback;
        }
    }

    /// <summary>
    /// Resolves config directory from the provider chain:
    /// explicit path → AppPaths.ConfigDir → WTA_SETTINGS_DIR env var → default LOCALAPPDATA.
    /// </summary>
    public static string ResolveConfigDir(AppPaths? appPaths, string? explicitPath = null)
    {
        return explicitPath
            ?? appPaths?.ConfigDir
            ?? Environment.GetEnvironmentVariable("WTA_SETTINGS_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinActivityTracker");
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

    private static void ClearRegistryValue(string valueName)
    {
        try { Registry.CurrentUser.CreateSubKey(RegKey).DeleteValue(valueName, throwOnMissingValue: false); }
        catch { }
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
        if (!Directory.Exists(fromDir))
        {
            Console.WriteLine(I18nService._("appPaths.sourceDirNotFound", fromDir));
            return;
        }

        if (!Directory.Exists(toDir))
        {
            try { Directory.CreateDirectory(toDir); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(I18nService._("appPaths.cannotCreateTarget", toDir, ex.Message));
                return;
            }
        }

        var files = Directory.GetFiles(fromDir, pattern);
        foreach (var file in files)
        {
            var dest = Path.Combine(toDir, Path.GetFileName(file));
            if (File.Exists(dest))
            {
                Console.WriteLine(I18nService._("appPaths.fileExistsSkip", Path.GetFileName(file)));
                continue;
            }

            try
            {
                File.Move(file, dest);
                Console.WriteLine(I18nService._("appPaths.fileMigrated", Path.GetFileName(file)));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(I18nService._("appPaths.migrationFailed", Path.GetFileName(file), ex.Message));
            }
        }

        // Clean up empty old directory (leave non-empty ones alone)
        try
        {
            if (Directory.GetFiles(fromDir).Length == 0
                && Directory.GetDirectories(fromDir).Length == 0)
            {
                Directory.Delete(fromDir);
                Console.WriteLine(I18nService._("appPaths.emptyDirCleaned", fromDir));
            }
        }
        catch { /* not critical */ }
    }
}
