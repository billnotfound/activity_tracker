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
    private readonly WriteQueue _writeQueue;
    private readonly IdleDetector _idleDetector;
    private readonly SettingsService _settings;
    private readonly ProcessNameCache _processCache;
    private readonly IconCacheService _iconCache;
    private readonly SystemPressure _systemPressure;
    private readonly TitleNormalizer _titleNormalizer;
    private readonly ILogger<WindowTracker> _logger;

    private string _currentProcess = string.Empty;
    private string _currentTitle = string.Empty;
    private DateTime _focusStart = DateTime.UtcNow;
    private DateTime _lastPollTime = DateTime.UtcNow;
    private bool _wasIdle;
    private DateTime? _idleStartedAt;

    private readonly Dictionary<IntPtr, (long DbId, string Process, string Title)> _openWindows = new();

    private readonly List<(IntPtr Hwnd, string ProcessName, string Title, bool IsFocused)> _reusableWindows = new();
    private readonly HashSet<IntPtr> _reusableHwnds = new();
    private readonly List<(IntPtr Hwnd, Models.WindowSession Session, string Process, string Title)> _reusablePendingAdds = new();
    private readonly List<long> _reusableIdsToClose = new();
    private readonly List<IntPtr> _reusableGoneHwnds = new();

    [ThreadStatic] private static StringBuilder? t_normalizeBuf;

    public WindowTracker(IServiceScopeFactory scopeFactory, WriteQueue writeQueue,
        IdleDetector idleDetector, SettingsService settings, ProcessNameCache processCache,
        IconCacheService iconCache, SystemPressure systemPressure, TitleNormalizer titleNormalizer,
        ILogger<WindowTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _writeQueue = writeQueue;
        _idleDetector = idleDetector;
        _settings = settings;
        _processCache = processCache;
        _iconCache = iconCache;
        _systemPressure = systemPressure;
        _titleNormalizer = titleNormalizer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WindowTracker started, interval: {Interval}s", _settings.Settings.WindowPollSeconds);

        await CloseOrphanSessions();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                if ((now - _lastPollTime).TotalSeconds > _settings.Settings.WindowPollSeconds * 3)
                {
                    _logger.LogDebug("Sleep gap detected: {Gap}s — resetting focus state",
                        (now - _lastPollTime).TotalSeconds);
                    _idleStartedAt = null;
                    if (_settings.Settings.TrackingEnabled)
                    {
                        SaveFocusChange(_lastPollTime);
                        _currentProcess = string.Empty;
                        _currentTitle = string.Empty;
                        _focusStart = now;
                    }
                }

                if (!_settings.Settings.TrackingEnabled)
                {
                    if (_wasIdle) FlushIdleEvent();
                    _wasIdle = false;
                    _idleStartedAt = null;
                    _lastPollTime = now;
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.WindowPollSeconds), stoppingToken);
                    continue;
                }

                var isFullscreen = _settings.Settings.FullscreenBypassIdle && IsForegroundFullscreen();
                var isIdle = isFullscreen ? false : _idleDetector.CheckIdle();

                if (isIdle && !_wasIdle)
                {
                    _logger.LogDebug("User went idle");
                    SaveFocusChange();
                    _wasIdle = true;
                    _idleStartedAt = DateTime.UtcNow;
                }
                else if (!isIdle)
                {
                    if (_wasIdle) FlushIdleEvent();
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

    private async Task CloseOrphanSessions()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var orphans = await db.WindowSessions
                .Where(w => w.CloseTime == null)
                .ToListAsync();

            if (orphans.Count > 0)
            {
                foreach (var o in orphans) o.CloseTime = now;
                await db.SaveChangesAsync();
                _logger.LogInformation("Closed {Count} orphan window sessions from previous run", orphans.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close orphan window sessions");
        }
    }

    private void TrackForegroundWindow()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        var title = GetWindowText(hWnd);
        if (string.IsNullOrEmpty(title)) return;

        var processName = _processCache.GetName((int)pid);
        if (processName == null) return;

        if (_settings.Settings.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return;

        if (processName != _currentProcess || title != _currentTitle)
        {
            SaveFocusChange();

            _currentProcess = processName;
            _currentTitle = title;
            _focusStart = DateTime.UtcNow;

            // Notify icon cache when a new process gains focus (event-driven, no polling)
            _iconCache.NotifyProcess(processName);

            _logger.LogDebug("Focus: {Process} - {Title}", processName, title);
        }
    }

    private void SaveFocusChange(DateTime? endTime = null)
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

        title = _titleNormalizer.Apply(process, title);

        _writeQueue.TryWrite(db =>
        {
            db.FocusChanges.Add(new FocusChange
            {
                Timestamp = start,
                ProcessName = process,
                WindowTitle = title,
                DurationSeconds = duration
            });
        });
    }

    private void FlushIdleEvent()
    {
        if (!_idleStartedAt.HasValue) return;
        var idleDuration = (DateTime.UtcNow - _idleStartedAt.Value).TotalSeconds;
        if (idleDuration >= 10)
        {
            var idleStart = _idleStartedAt.Value;
            _writeQueue.TryWrite(db =>
            {
                db.SystemEvents.Add(new SystemEvent
                {
                    EventType = SystemEventTypes.Idle,
                    Timestamp = idleStart,
                    DurationSeconds = idleDuration
                });
            });
        }
        _idleStartedAt = null;
    }

    private async Task SyncWindowSessions()
    {
        if (_systemPressure.GetCurrent() != PressureLevel.Normal)
        {
            _logger.LogDebug("Skipping window session sync (pressure: {Pressure}, fill: {Fill}%)",
                _systemPressure.GetCurrent(), _writeQueue.ChannelFillPercent);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var excluded = _settings.Settings.ExcludedProcesses;
        var hasChanges = false;

        EnumerateVisibleWindowsInto(_reusableWindows, _processCache);

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
                    hasChanges = true;
                }
            }
            else
            {
                var s = new Models.WindowSession { ProcessName = process, WindowTitle = title, OpenTime = now };
                db.WindowSessions.Add(s);
                _reusablePendingAdds.Add((hwnd, s, process, title));
                hasChanges = true;
            }
        }

        _reusableGoneHwnds.Clear();
        foreach (var (hwnd, entry) in _openWindows)
        {
            if (!_reusableHwnds.Contains(hwnd))
            {
                _reusableIdsToClose.Add(entry.DbId);
                _reusableGoneHwnds.Add(hwnd);
                hasChanges = true;
            }
        }

        if (_reusableIdsToClose.Count > 0)
        {
            var toClose = await db.WindowSessions.Where(s => _reusableIdsToClose.Contains(s.Id)).ToListAsync();
            foreach (var s in toClose) s.CloseTime = now;
            _reusableIdsToClose.Clear();
        }

        if (hasChanges)
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

    private static void EnumerateVisibleWindowsInto(
        List<(IntPtr Hwnd, string ProcessName, string Title, bool IsFocused)> results,
        ProcessNameCache cache)
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
            var processName = cache.GetName((int)pid);
            if (processName == null) return true;

            results.Add((hWnd, processName, title, hWnd == foregroundHwnd));
            return true;
        }, IntPtr.Zero);
    }

    public static List<(string ProcessName, string Title, bool IsFocused)> EnumerateVisibleWindows(
        ProcessNameCache cache)
    {
        var raw = new List<(IntPtr, string, string, bool)>();
        EnumerateVisibleWindowsInto(raw, cache);
        var results = new List<(string, string, bool)>(raw.Count);
        foreach (var (_, process, title, focused) in raw)
            results.Add((process, title, focused));
        return results;
    }

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

    internal static string NormalizeTitle(string title)
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
