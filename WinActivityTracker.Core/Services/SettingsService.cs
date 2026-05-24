// Singleton service that loads/saves TrackerSettings from a JSON file next to the database.
// Trackers inject this and read Settings on each poll, so PUT /api/settings takes effect immediately.
// The Update() method enforces minimum values to prevent misconfiguration (e.g. polling every 0 seconds).
// File path: %LOCALAPPDATA%\WinActivityTracker\settings.json
using System.Text.Json;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Services;

public class SettingsService
{
    private readonly string _filePath;
    private TrackerSettings _settings = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // The current settings. Trackers read this on every poll cycle.
    // Replacing the entire object reference would break the live-update contract,
    // so Update() mutates fields in-place.
    public TrackerSettings Settings => _settings;

    public SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinActivityTracker");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        Load();
    }

    // Silently falls back to defaults if the file is missing or corrupted.
    // This ensures first-run without manual setup.
    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _settings = JsonSerializer.Deserialize<TrackerSettings>(json) ?? new();
            }
        }
        catch { }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    // Applies new settings with safety floors:
    // - Window poll min 1s to avoid CPU saturation
    // - Process poll min 5s (enumeration is expensive with hundreds of processes)
    // - Media poll min 1s
    // - Idle threshold min 1 minute
    // - Retention min 1 day (0 would delete everything)
    public void Update(TrackerSettings newSettings)
    {
        _settings.TrackingEnabled = newSettings.TrackingEnabled;
        _settings.WindowPollSeconds = Math.Max(1, newSettings.WindowPollSeconds);
        _settings.ProcessPollSeconds = Math.Max(5, newSettings.ProcessPollSeconds);
        _settings.MediaPollSeconds = Math.Max(1, newSettings.MediaPollSeconds);
        _settings.IdleThresholdMinutes = Math.Max(1, newSettings.IdleThresholdMinutes);
        _settings.ExcludedProcesses = newSettings.ExcludedProcesses ?? [];
        _settings.DataRetentionDays = Math.Max(1, newSettings.DataRetentionDays);
        _settings.AutoCleanup = newSettings.AutoCleanup;
        Save();
    }
}
