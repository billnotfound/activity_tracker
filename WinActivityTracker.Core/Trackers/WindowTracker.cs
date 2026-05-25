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
    private DateTime _lastPollTime = DateTime.UtcNow;
    private bool _wasIdle;

    // Window session tracking: HWND → (dbId, process, title).
    // Each visible window gets ONE WindowSession row in the DB. When the window
    // disappears, CloseTime is updated. This replaces the old point-in-time snapshots.
    private readonly Dictionary<IntPtr, (long DbId, string Process, string Title)> _openWindows = new();

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

                // Detect sleep/hibernate by wall-clock gap between polls.
                // Normal gap ≤ poll interval + small jitter. A gap > 3x means
                // the system was suspended and we should not count that time.
                var now = DateTime.UtcNow;
                var expectedMax = TimeSpan.FromSeconds(_settings.Settings.WindowPollSeconds * 3);
                if ((now - _lastPollTime) > expectedMax)
                {
                    _logger.LogDebug("Sleep gap detected: {Gap}s", (now - _lastPollTime).TotalSeconds);
                    await SaveFocusChange();  // end pre-sleep session
                    await InsertSleepGap(_lastPollTime, now);
                    _currentProcess = string.Empty;
                    _currentTitle = string.Empty;
                    _focusStart = now;
                }
                _lastPollTime = now;

                // Fullscreen/maximized windows (games, videos) → skip idle detection.
                // Controlled by settings.FullscreenBypassIdle (default: true).
                var isFullscreen = _settings.Settings.FullscreenBypassIdle && IsForegroundFullscreen();
                var isIdle = isFullscreen ? false : _idleDetector.CheckIdle();

                if (isIdle && !_wasIdle)
                {
                    _logger.LogDebug("User went idle");
                    await SaveFocusChange();
                    _wasIdle = true;
                }
                else if (!isIdle)
                {
                    _wasIdle = false;
                    TrackForegroundWindow();
                    await SyncWindowSessions();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WindowTracker poll error");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.WindowPollSeconds), stoppingToken);
        }

        // Graceful shutdown: mark all remaining open windows as closed
        await CloseAllWindowSessions();
    }

    // Inserts a __SystemSleep marker for the detected sleep period.
    // Called from the poll loop when wall-clock gap between polls exceeds 3x the interval.
    private async Task InsertSleepGap(DateTime sleepStart, DateTime wakeTime)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        db.FocusChanges.Add(new Models.FocusChange
        {
            Timestamp = sleepStart,
            ProcessName = "__SystemSleep",
            WindowTitle = "Sleep gap detected",
            DurationSeconds = (wakeTime - sleepStart).TotalSeconds
        });
        await db.SaveChangesAsync();
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

    // Session-based window tracking — replaces the old SaveVisibleWindows() which
    // wrote a full snapshot every poll. Now we detect window open/close transitions.
    //
    // New window (HWND not in _openWindows) → INSERT WindowSession, OpenTime=now.
    // Title changed for same HWND         → UPDATE old CloseTime, INSERT new session.
    // Window gone (HWND no longer visible) → UPDATE CloseTime=now.
    //
    // Data volume goes from ~100K rows/day to ~100-500 rows/day.
    private async Task SyncWindowSessions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var excluded = _settings.Settings.ExcludedProcesses;

        var currentWindows = EnumerateVisibleWindowsWithHwnd();
        var currentHwnds = new HashSet<IntPtr>();

        foreach (var (hwnd, process, title, _) in currentWindows)
        {
            if (excluded.Contains(process, StringComparer.OrdinalIgnoreCase)) continue;
            currentHwnds.Add(hwnd);

            if (_openWindows.TryGetValue(hwnd, out var existing))
            {
                // Title changed → close old session, open new one.
                // Strip Braille spinner chars (U+2800–U+28FF) before comparing —
                // terminal titles change every poll due to progress spinners.
                if (NormalizeTitle(existing.Title) != NormalizeTitle(title))
                {
                    var old = await db.WindowSessions.FindAsync(existing.DbId);
                    if (old != null) old.CloseTime = now;
                    var s = new Models.WindowSession { ProcessName = process, WindowTitle = title, OpenTime = now };
                    db.WindowSessions.Add(s);
                    await db.SaveChangesAsync();
                    _openWindows[hwnd] = (s.Id, process, title);
                }
            }
            else
            {
                // New window opened
                var s = new Models.WindowSession { ProcessName = process, WindowTitle = title, OpenTime = now };
                db.WindowSessions.Add(s);
                await db.SaveChangesAsync();
                _openWindows[hwnd] = (s.Id, process, title);
            }
        }

        // Close windows that disappeared
        var gone = new List<IntPtr>();
        foreach (var (hwnd, entry) in _openWindows)
        {
            if (!currentHwnds.Contains(hwnd))
            {
                var closed = await db.WindowSessions.FindAsync(entry.DbId);
                if (closed != null) closed.CloseTime = now;
                gone.Add(hwnd);
            }
        }
        foreach (var hwnd in gone) _openWindows.Remove(hwnd);
        if (gone.Count > 0) await db.SaveChangesAsync();
    }

    // Called on shutdown — close all remaining open sessions.
    private async Task CloseAllWindowSessions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        foreach (var (_, entry) in _openWindows)
        {
            var s = await db.WindowSessions.FindAsync(entry.DbId);
            if (s != null) s.CloseTime = now;
        }
        _openWindows.Clear();
        await db.SaveChangesAsync();
    }

    // Like EnumerateVisibleWindows but also returns HWND for session tracking.
    private static List<(IntPtr Hwnd, string ProcessName, string Title, bool IsFocused)> EnumerateVisibleWindowsWithHwnd()
    {
        var results = new List<(IntPtr, string, string, bool)>();
        var foregroundHwnd = NativeMethods.GetForegroundWindow();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
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
            catch { return true; }

            results.Add((hWnd, processName, title, hWnd == foregroundHwnd));
            return true;
        }, IntPtr.Zero);

        return results;
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

    // Returns true if the foreground window is maximized or fullscreen.
    // Uses GetWindowPlacement (detects maximized windows) and
    // GetWindowRect vs screen bounds (detects borderless fullscreen).
    private static bool IsForegroundFullscreen()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return false;

        // Check maximized state
        var wp = new NativeMethods.WINDOWPLACEMENT();
        wp.length = (uint)Marshal.SizeOf(wp);
        if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
        {
            if (wp.showCmd == NativeMethods.SW_SHOWMAXIMIZED)
                return true;
        }

        // Check borderless fullscreen: window rect covers the monitor
        if (NativeMethods.GetWindowRect(hWnd, out var wr))
        {
            var hMon = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf(mi);
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                if (wr.Width >= mi.rcMonitor.Width && wr.Height >= mi.rcMonitor.Height)
                    return true;
            }
        }

        return false;
    }

    // Strips Braille Patterns (U+2800–U+28FF) used as terminal progress spinners.
// These glyphs change every poll cycle and would otherwise create a new
// WindowSession on every poll (spurious "title changed" events).
private static string NormalizeTitle(string title)
{
    if (string.IsNullOrEmpty(title)) return title;
    var result = new StringBuilder(title.Length);
    foreach (var c in title)
        if (c < '⠀' || c > '⣿')
            result.Append(c);
    return result.ToString();
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
