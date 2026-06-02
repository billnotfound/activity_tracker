// Runtime configuration persisted to %LOCALAPPDATA%\WinActivityTracker\settings.json.
// Trackers read from SettingsService.Settings on each poll cycle, so changes via
// PUT /api/settings take effect immediately without restart.
// Minimum values are enforced by SettingsService.Update() to prevent accidental 0-interval loops.
using System.Text.Json.Serialization;

namespace WinActivityTracker.Core.Models;

public class TrackerSettings
{
    // When false, all trackers skip their polling logic. API still works.
    // Use this to pause tracking without stopping the service.
    public bool TrackingEnabled { get; set; } = true;

    // When true, fullscreen/maximized windows bypass idle detection.
    // Prevents AFK timeout during gaming, video, or presentations.
    public bool FullscreenBypassIdle { get; set; } = true;

    // When true, consecutive same-process FocusChanges count as 1 switch.
    // Firefox tabs, IDE windows, etc. won't inflate the switch count.
    public bool MergeSameProcessSwitches { get; set; } = true;

    // How often WindowTracker enumerates visible windows and checks focus (seconds). Min: 1.
    public int WindowPollSeconds { get; set; } = 3;

    // How often ProcessTracker snapshots background processes (seconds). Min: 5.
    public int ProcessPollSeconds { get; set; } = 30;

    // How often MediaSessionTracker polls Windows Media Control (seconds). Min: 1.
    public int MediaPollSeconds { get; set; } = 5;

    // User must be inactive this many minutes before tracking is paused. Min: 1.
    public int IdleThresholdMinutes { get; set; } = 2;

    // Process names (case-insensitive) to exclude from ALL tracking — focus, visible windows, background, and media.
    public List<string> ExcludedProcesses { get; set; } = [];

    // /api/db/cleanup deletes records older than this (days). Also used as default for the ?days= parameter.
    public int DataRetentionDays { get; set; } = 90;

    // Reserved: when true, cleanup runs automatically on a daily schedule. Not yet wired.
    public bool AutoCleanup { get; set; } = true;

    // API server port. Requires restart to take effect (not hot-reloadable).
    // Min: 1024, Max: 65535, Default: 5200.
    public int ApiPort { get; set; } = 5200;

    // Whether the program is registered for auto-start (HKCU\...\Run).
    // Synced with registry on startup; toggled by both tray menu and settings window.
    public bool AutoStartEnabled { get; set; }
}
