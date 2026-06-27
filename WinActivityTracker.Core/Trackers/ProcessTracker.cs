using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Interop;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Core.Trackers;

public class ProcessTracker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settings;
    private readonly ProcessNameCache _processCache;
    private readonly IconService _iconService;
    private readonly SystemPressure _systemPressure;
    private readonly ILogger<ProcessTracker> _logger;

    private readonly Dictionary<int, (long DbId, string Name)> _runningProcesses = new();

    private readonly HashSet<int> _reusablePids = new();
    private readonly HashSet<int> _reusableVisiblePids = new();
    private readonly List<(string Name, int Id)> _reusableBg = new();
    private readonly List<long> _reusableGoneIds = new();
    private readonly List<int> _reusableGonePids = new();
    private readonly List<(int Pid, string Name, Models.ProcessSession Session)> _reusableNewSessions = new();

    public ProcessTracker(IServiceScopeFactory scopeFactory, SettingsService settings,
        ProcessNameCache processCache, IconService iconService,
        SystemPressure systemPressure, ILogger<ProcessTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _processCache = processCache;
        _iconService = iconService;
        _systemPressure = systemPressure;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcessTracker started, interval: {Interval}s", _settings.Settings.ProcessPollSeconds);

        // Close orphan sessions from a previous crash (EndTime == null).
        await CloseOrphanSessions();

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_settings.Settings.TrackingEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.ProcessPollSeconds), stoppingToken);
                    continue;
                }

                await SyncProcessSessions();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessTracker poll error");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.ProcessPollSeconds), stoppingToken);
        }

        await CloseAllProcessSessions();
    }

    private async Task CloseOrphanSessions()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var orphans = await db.ProcessSessions
                .Where(p => p.EndTime == null)
                .ToListAsync();

            if (orphans.Count > 0)
            {
                foreach (var o in orphans) o.EndTime = now;
                await db.SaveChangesAsync();
                _logger.LogInformation("Closed {Count} orphan process sessions from previous run", orphans.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close orphan process sessions");
        }
    }

    private async Task SyncProcessSessions()
    {
        var pressure = _systemPressure.GetCurrent();
        if (pressure >= PressureLevel.Critical)
        {
            _logger.LogDebug("Skipping process session sync (critical pressure)");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var excluded = _settings.Settings.ExcludedProcesses;
        var hasChanges = false;

        // Cache maintained by WMI events when active, otherwise poll.
        if (!_processCache.IsWmiActive)
            _processCache.RefreshAll();
        var processMap = _processCache.Snapshot;

        // Find PIDs with visible top-level windows.
        _reusableVisiblePids.Clear();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != 0) _reusableVisiblePids.Add((int)pid);
            return true;
        }, IntPtr.Zero);

        _reusableBg.Clear();
        foreach (var (pid, entry) in processMap)
        {
            if (!_reusableVisiblePids.Contains(pid))
                _reusableBg.Add((entry.Name, pid));
        }

        _reusablePids.Clear();
        foreach (var (_, pid) in _reusableBg) _reusablePids.Add(pid);

        _reusableNewSessions.Clear();
        foreach (var (name, pid) in _reusableBg)
        {
            if (_runningProcesses.ContainsKey(pid))
                continue;
            if (excluded.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            var s = new Models.ProcessSession { ProcessName = name, ProcessId = pid, StartTime = now };
            db.ProcessSessions.Add(s);
            _reusableNewSessions.Add((pid, name, s));
            hasChanges = true;
        }

        _reusableGoneIds.Clear();
        _reusableGonePids.Clear();
        foreach (var (pid, entry) in _runningProcesses)
        {
            if (!_reusablePids.Contains(pid)
                || excluded.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                _reusableGoneIds.Add(entry.DbId);
                _reusableGonePids.Add(pid);
                hasChanges = true;
            }
        }

        if (_reusableGoneIds.Count > 0)
        {
            var endedSessions = await db.ProcessSessions
                .Where(p => _reusableGoneIds.Contains(p.Id))
                .ToListAsync();
            foreach (var s in endedSessions) s.EndTime = now;
        }

        if (hasChanges)
            await db.SaveChangesAsync();

        foreach (var (pid, name, session) in _reusableNewSessions)
            _runningProcesses[pid] = (session.Id, name);

        foreach (var pid in _reusableGonePids) _runningProcesses.Remove(pid);

        // Icon collection for visible processes (fire-and-forget) — only in Normal pressure.
        if (pressure == PressureLevel.Normal)
        {
            var visiblePids = _reusableVisiblePids.ToList();
            _ = _iconService.EnsureIconsAsync(visiblePids);
        }
    }

    private async Task CloseAllProcessSessions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var ids = _runningProcesses.Values.Select(e => e.DbId).ToList();
        if (ids.Count > 0)
        {
            var sessions = await db.ProcessSessions.Where(p => ids.Contains(p.Id)).ToListAsync();
            foreach (var s in sessions) s.EndTime = now;
            await db.SaveChangesAsync();
        }
        _runningProcesses.Clear();
    }
}
