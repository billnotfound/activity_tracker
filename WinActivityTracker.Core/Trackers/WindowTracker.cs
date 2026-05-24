// Core tracker: monitors the foreground window and all visible top-level windows.
//
// Lifecycle:
//   Every WindowPollSeconds (default 3s), it:
//   1. Checks idle state via IdleDetector
//   2. If not idle, gets the foreground window (GetForegroundWindow)
//   3. Compares with the previous foreground window — if different, writes a FocusChange record
//      for the OLD window (duration = now - focusStart)
//   4. Enumerates all visible windows via EnumWindows and writes a WindowSnapshot batch
//
// Focus duration is calculated retroactively: when the user switches FROM app A TO app B,
// app A's FocusChange is INSERTed with DurationSeconds = time spent on A. This means the
// CURRENT foreground window has NO corresponding FocusChange row until the user switches away.
// On shutdown/crash, the last focus session is lost.
//
// Excluded processes (from settings) are filtered out at two points:
//   - Focus tracking: excluded processes don't trigger focus changes
//   - Visible window snapshots: excluded processes aren't written to the DB
//
// Thread safety: uses IServiceScopeFactory to create a new scope per poll cycle.
// EF Core DbContext is NOT thread-safe; scoping ensures each tracker has its own instance.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Interop;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Core.Trackers;

public class WindowTracker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdleDetector _idleDetector;
    private readonly SettingsService _settings;
    private readonly ILogger<WindowTracker> _logger;

    // Tracks the CURRENT foreground window state. When these change, the OLD session is persisted.
    private string _currentProcess = string.Empty;
    private string _currentTitle = string.Empty;
    private DateTime _focusStart = DateTime.UtcNow;
    private bool _wasIdle;  // Tracks idle transitions to avoid repeated SaveFocusChange calls

    public WindowTracker(IServiceScopeFactory scopeFactory, IdleDetector idleDetector, SettingsService settings, ILogger<WindowTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _idleDetector = idleDetector;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WindowTracker started, interval: {Interval}s", _settings.Settings.WindowPollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_settings.Settings.TrackingEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.WindowPollSeconds), stoppingToken);
                    continue;
                }

                var isIdle = _idleDetector.CheckIdle();

                if (isIdle && !_wasIdle)
                {
                    // Just went idle — persist the current focus session so AFK time isn't counted
                    _logger.LogDebug("User went idle");
                    await SaveFocusChange();
                    _wasIdle = true;
                }
                else if (!isIdle)
                {
                    _wasIdle = false;
                    TrackForegroundWindow();
                    await SaveVisibleWindows();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WindowTracker poll error");
            }

            // Re-read interval each iteration so settings changes take effect without restart
            await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.WindowPollSeconds), stoppingToken);
        }
    }

    private void TrackForegroundWindow()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        var title = GetWindowText(hWnd);
        if (string.IsNullOrEmpty(title)) return;

        string processName;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            processName = proc.ProcessName;
        }
        catch { return; }  // Process already exited or access denied

        // Skip excluded processes — they don't trigger focus changes
        if (_settings.Settings.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return;

        // Is this a different window than the one we're currently tracking?
        if (processName != _currentProcess || title != _currentTitle)
        {
            // Fire-and-forget: we don't await here because the next poll might arrive
            // before the DB write completes. The duration was already snapshotted.
            _ = SaveFocusChange();

            _currentProcess = processName;
            _currentTitle = title;
            _focusStart = DateTime.UtcNow;

            _logger.LogDebug("Focus: {Process} - {Title}", processName, title);
        }
    }

    // Persists the PREVIOUS focus session (the one ending now).
    // Called on: focus change, idle transition, and eventually on graceful shutdown.
    private async Task SaveFocusChange()
    {
        if (string.IsNullOrEmpty(_currentProcess)) return;

        var duration = (DateTime.UtcNow - _focusStart).TotalSeconds;
        if (duration < 0.5) return;  // Filter out accidental sub-second switches

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.FocusChanges.Add(new FocusChange
        {
            Timestamp = _focusStart,
            ProcessName = _currentProcess,
            WindowTitle = _currentTitle,
            DurationSeconds = duration
        });

        await db.SaveChangesAsync();

        // Reset so we don't re-save the same session
        _currentProcess = string.Empty;
        _currentTitle = string.Empty;
    }

    // Writes a batch of WindowSnapshot rows — one per visible top-level window.
    // Runs every poll cycle, so this table grows quickly. Use /api/db/cleanup.
    private async Task SaveVisibleWindows()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var excluded = _settings.Settings.ExcludedProcesses;

        var windows = EnumerateVisibleWindows();
        foreach (var (process, title, isFocused) in windows)
        {
            if (excluded.Contains(process, StringComparer.OrdinalIgnoreCase)) continue;

            db.WindowSnapshots.Add(new WindowSnapshot
            {
                Timestamp = now,
                ProcessName = process,
                WindowTitle = title,
                IsFocused = isFocused
            });
        }

        await db.SaveChangesAsync();
    }

    // Static so it can be called from API endpoints (GET /api/windows/current)
    // without needing a WindowTracker instance.
    public static List<(string ProcessName, string Title, bool IsFocused)> EnumerateVisibleWindows()
    {
        var results = new List<(string, string, bool)>();
        var foregroundHwnd = NativeMethods.GetForegroundWindow();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            // Filter: must be visible, not minimized, and have a non-empty title.
            // This excludes UWP frame hosts, invisible overlay windows, etc.
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (NativeMethods.IsIconic(hWnd)) return true;

            var title = GetWindowText(hWnd);
            if (string.IsNullOrEmpty(title)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch { return true; }  // Process exited mid-enumeration — skip it

            results.Add((processName, title, hWnd == foregroundHwnd));
            return true;  // Continue enumeration
        }, IntPtr.Zero);

        return results;
    }

    private static string GetWindowText(IntPtr hWnd)
    {
        var length = NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
