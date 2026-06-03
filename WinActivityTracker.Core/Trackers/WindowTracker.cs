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
using Microsoft.EntityFrameworkCore;
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

    // Reusable collections to reduce per-cycle allocations.
    private readonly List<(IntPtr Hwnd, string ProcessName, string Title, bool IsFocused)> _reusableWindows = new();
    private readonly HashSet<IntPtr> _reusableHwnds = new();
    private readonly List<(IntPtr Hwnd, Models.WindowSession Session, string Process, string Title)> _reusablePendingAdds = new();
    private readonly List<long> _reusableIdsToClose = new();
    private readonly List<IntPtr> _reusableGoneHwnds = new();

    // Reusable StringBuilder — [ThreadStatic] avoids lock contention.
    [ThreadStatic] private static StringBuilder? t_normalizeBuf;

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
                var now = DateTime.UtcNow;

                if ((now - _lastPollTime).TotalSeconds > _settings.Settings.WindowPollSeconds * 3)
                {
                    _logger.LogDebug("Sleep gap detected: {Gap}s — resetting focus state",
                        (now - _lastPollTime).TotalSeconds);
                    if (_settings.Settings.TrackingEnabled)
                    {
                        // Close the pre-sleep focus session using _lastPollTime as the
                        // approximate end (at most 3s before actual sleep). HeartbeatService
                        // handles the __SystemSleep FocusChange and SystemEvents table.
                        await SaveFocusChange(_lastPollTime);
                        _currentProcess = string.Empty;
                        _currentTitle = string.Empty;
                        _focusStart = now;
                    }
                }

                if (!_settings.Settings.TrackingEnabled)
                {
                    _lastPollTime = now;
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.WindowPollSeconds), stoppingToken);
                    continue;
                }

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

            _lastPollTime = DateTime.UtcNow;
            await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.WindowPollSeconds), stoppingToken);
        }

        await CloseAllWindowSessions();
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
        catch { return; }

        if (_settings.Settings.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return;

        if (processName != _currentProcess || title != _currentTitle)
        {
            _ = SaveFocusChange();

            _currentProcess = processName;
            _currentTitle = title;
            _focusStart = DateTime.UtcNow;

            _logger.LogDebug("Focus: {Process} - {Title}", processName, title);
        }
    }

    private async Task SaveFocusChange(DateTime? endTime = null)
    {
        var process = _currentProcess;
        var title = _currentTitle;
        var start = _focusStart;

        if (string.IsNullOrEmpty(process)) return;

        var end = endTime ?? DateTime.UtcNow;
        var duration = (end - start).TotalSeconds;
        if (duration < 0.5) return;

        _currentProcess = string.Empty;
        _currentTitle = string.Empty;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.FocusChanges.Add(new FocusChange
        {
            Timestamp = start,
            ProcessName = process,
            WindowTitle = title,
            DurationSeconds = duration
        });

        await db.SaveChangesAsync();
    }

    private async Task SyncWindowSessions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var excluded = _settings.Settings.ExcludedProcesses;

        var processMap = BuildProcessNameMap();
        EnumerateVisibleWindowsInto(_reusableWindows, processMap);

        _reusableHwnds.Clear();
        _reusablePendingAdds.Clear();
        _reusableIdsToClose.Clear();

        foreach (var (hwnd, process, title, _) in _reusableWindows)
        {
            if (excluded.Contains(process, StringComparer.OrdinalIgnoreCase)) continue;
            _reusableHwnds.Add(hwnd);

            if (_openWindows.TryGetValue(hwnd, out var existing))
            {
                if (NormalizeTitle(existing.Title) != NormalizeTitle(title))
                {
                    _reusableIdsToClose.Add(existing.DbId);
                    var s = new Models.WindowSession { ProcessName = process, WindowTitle = title, OpenTime = now };
                    db.WindowSessions.Add(s);
                    _reusablePendingAdds.Add((hwnd, s, process, title));
                }
            }
            else
            {
                var s = new Models.WindowSession { ProcessName = process, WindowTitle = title, OpenTime = now };
                db.WindowSessions.Add(s);
                _reusablePendingAdds.Add((hwnd, s, process, title));
            }
        }

        _reusableGoneHwnds.Clear();
        foreach (var (hwnd, entry) in _openWindows)
        {
            if (!_reusableHwnds.Contains(hwnd))
            {
                _reusableIdsToClose.Add(entry.DbId);
                _reusableGoneHwnds.Add(hwnd);
            }
        }

        if (_reusableIdsToClose.Count > 0)
        {
            var toClose = await db.WindowSessions.Where(s => _reusableIdsToClose.Contains(s.Id)).ToListAsync();
            foreach (var s in toClose) s.CloseTime = now;
            _reusableIdsToClose.Clear();
        }

        await db.SaveChangesAsync();

        foreach (var (hwnd, session, process, title) in _reusablePendingAdds)
            _openWindows[hwnd] = (session.Id, process, title);

        foreach (var hwnd in _reusableGoneHwnds) _openWindows.Remove(hwnd);
    }

    private async Task CloseAllWindowSessions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var ids = _openWindows.Values.Select(e => e.DbId).ToList();
        if (ids.Count > 0)
        {
            var sessions = await db.WindowSessions.Where(s => ids.Contains(s.Id)).ToListAsync();
            foreach (var s in sessions) s.CloseTime = now;
            await db.SaveChangesAsync();
        }
        _openWindows.Clear();
    }

    // Fills the reusable list — avoids new list allocation each poll cycle.
    private static void EnumerateVisibleWindowsInto(
        List<(IntPtr Hwnd, string ProcessName, string Title, bool IsFocused)> results,
        Dictionary<int, string> processMap)
    {
        results.Clear();
        var foregroundHwnd = NativeMethods.GetForegroundWindow();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (NativeMethods.IsIconic(hWnd)) return true;

            var title = GetWindowText(hWnd);
            if (string.IsNullOrEmpty(title)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (!processMap.TryGetValue((int)pid, out var processName)) return true;

            results.Add((hWnd, processName, title, hWnd == foregroundHwnd));
            return true;
        }, IntPtr.Zero);
    }

    // Static so it can be called from API endpoints (GET /api/windows/current)
    // without needing a WindowTracker instance.
    public static List<(string ProcessName, string Title, bool IsFocused)> EnumerateVisibleWindows()
    {
        var map = BuildProcessNameMap();
        var results = new List<(string, string, bool)>();
        var foregroundHwnd = NativeMethods.GetForegroundWindow();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (NativeMethods.IsIconic(hWnd)) return true;

            var title = GetWindowText(hWnd);
            if (string.IsNullOrEmpty(title)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (!map.TryGetValue((int)pid, out var processName)) return true;

            results.Add((processName, title, hWnd == foregroundHwnd));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    // Cached PID→Name snapshot. Returns the dictionary directly rather than
    // creating a copy — callers that only read it don't need their own copy.
    private static (Dictionary<int, string> Map, DateTime Timestamp) s_cachedProcessMap;
    private static readonly object s_processMapLock = new();

    private static Dictionary<int, string> BuildProcessNameMap()
    {
        var now = DateTime.UtcNow;
        Dictionary<int, string> map;
        lock (s_processMapLock)
        {
            if ((now - s_cachedProcessMap.Timestamp).TotalSeconds < 2 && s_cachedProcessMap.Map.Count > 0)
                return s_cachedProcessMap.Map;
        }

        map = new Dictionary<int, string>();
        try
        {
            var procs = Process.GetProcesses();
            foreach (var p in procs)
            {
                try { map[p.Id] = p.ProcessName; } catch { }
                try { p.Dispose(); } catch { }
            }
        }
        catch { }

        lock (s_processMapLock) { s_cachedProcessMap = (map, now); }
        return map;
    }

    // Returns true if the foreground window is maximized or fullscreen.
    private static bool IsForegroundFullscreen()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return false;

        var wp = new NativeMethods.WINDOWPLACEMENT();
        wp.length = (uint)Marshal.SizeOf(wp);
        if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
        {
            if (wp.showCmd == NativeMethods.SW_SHOWMAXIMIZED)
                return true;
        }

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
    // Uses a [ThreadStatic] pooled StringBuilder to avoid per-call allocation.
    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return title;

        var buf = t_normalizeBuf;
        if (buf == null)
        {
            buf = new StringBuilder(title.Length);
            t_normalizeBuf = buf;
        }
        else
        {
            buf.Clear();
            buf.EnsureCapacity(title.Length);
        }

        foreach (var c in title)
            if ((int)c < 0x2800 || (int)c > 0x28FF)
                buf.Append(c);
        return buf.ToString();
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
