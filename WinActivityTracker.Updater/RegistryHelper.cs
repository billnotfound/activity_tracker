using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace WinActivityTracker.Updater;

/// <summary>
/// Reads and writes registry keys for the updater.
/// Supports test mode via WTA_UPDATER_TESTING env var.
/// </summary>
internal static class RegistryHelper
{
    private const string AppRegKey = @"Software\WinActivityTracker";
    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "taskmonitor114";
    private const string DataDirValue = "DataDir";
    private const string ConfigDirValue = "ConfigDir";

    private static readonly string TestAppRegKey = $"{AppRegKey}\\UpdaterTest";
    private static readonly string TestAutoStartValue = "taskmonitor114-test";

    public static bool IsTestMode { get; } =
        Environment.GetEnvironmentVariable("WTA_UPDATER_TESTING") == "1";

    private static string EffectiveAppRegKey => IsTestMode ? TestAppRegKey : AppRegKey;
    private static string EffectiveAutoStartValue => IsTestMode ? TestAutoStartValue : AutoStartValue;

    /// <summary>
    /// Read a value from HKCU\Software\WinActivityTracker.
    /// </summary>
    public static string? ReadAppRegistry(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(EffectiveAppRegKey);
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// Read the auto-start exe path from HKCU\...\Run\taskmonitor114.
    /// The value format is: "<exe-path>" --autostart
    /// </summary>
    public static string? ReadAutoStartExePath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey);
            var value = key?.GetValue(EffectiveAutoStartValue) as string;
            return ParseExePathFromAutoStartValue(value);
        }
        catch { return null; }
    }

    /// <summary>
    /// Update the auto-start registry to point to newExePath.
    /// Format matches TrayApplicationContext.WriteRegistryAutoStart: "<path>" --autostart
    /// </summary>
    public static void UpdateAutoStart(string newExePath)
    {
        if (IsTestMode)
        {
            Console.WriteLine($"[TEST] Would update auto-start: {newExePath}");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, writable: true);
            if (key == null) return;

            var currentValue = key.GetValue(EffectiveAutoStartValue) as string;
            var currentPath = ParseExePathFromAutoStartValue(currentValue);
            if (currentPath != null &&
                string.Equals(currentPath, newExePath, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"自启动注册表路径未变，跳过更新。");
                return;
            }

            var newValue = $"\"{newExePath}\" --autostart";
            key.SetValue(EffectiveAutoStartValue, newValue);
            Console.WriteLine($"已更新自启动注册表: {newExePath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"自启动注册表更新失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Write ConfigDir and DataDir to HKCU\Software\WinActivityTracker.
    /// Used for the "no old version found" fresh-install path.
    /// </summary>
    public static void WriteConfigDir(string configDir)
    {
        if (IsTestMode)
        {
            Console.WriteLine($"[TEST] Would write ConfigDir: {configDir}");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(EffectiveAppRegKey);
            key.SetValue(ConfigDirValue, configDir);
            key.SetValue(DataDirValue, configDir);
            Console.WriteLine($"已写入配置路径: {configDir}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"写入注册表失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse exe path from auto-start value: "<path>" --autostart → path
    /// </summary>
    private static string? ParseExePathFromAutoStartValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value, "\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}
