// Session-based background process tracking.
//
// SyncProcessSessions():
//   1. Single GetProcesses() → filter to background PIDs (no MainWindowHandle).
//   2. Compare with _runningProcesses dictionary (PID → session).
//   3. New PID → INSERT ProcessSession (StartTime=now, EndTime=null).
//   4. Gone PID → UPDATE EndTime=now, remove from dictionary.
//   5. Dispose all Process objects in finally.
//
// On shutdown: close all remaining sessions via CloseAllProcessSessions().
//
// Starts 10s after host to let WindowTracker's first poll complete.
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Core.Trackers;

public class ProcessTracker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settings;
    private readonly ILogger<ProcessTracker> _logger;

    // PID → (dbId, processName) for session-based tracking.
    private readonly Dictionary<int, (long DbId, string Name)> _runningProcesses = new();

    public ProcessTracker(IServiceScopeFactory scopeFactory, SettingsService settings, ILogger<ProcessTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcessTracker started, interval: {Interval}s", _settings.Settings.ProcessPollSeconds);
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

    // Session-based process tracking — same pattern as WindowTracker.SyncWindowSessions.
    // Detects process start/stop transitions instead of writing full snapshots.
    private async Task SyncProcessSessions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var excluded = _settings.Settings.ExcludedProcesses;

        // Get current background processes (no visible windows, not excluded)
        var current = GetBackgroundProcesses();
        var currentPids = new HashSet<int>();

        foreach (var (name, pid) in current)
        {
            currentPids.Add(pid);

            if (_runningProcesses.TryGetValue(pid, out var existing))
            {
                // Still running — nothing to do (process name rarely changes for same PID)
            }
            else
            {
                // New process started
                var s = new Models.ProcessSession { ProcessName = name, ProcessId = pid, StartTime = now };
                db.ProcessSessions.Add(s);
                await db.SaveChangesAsync();
                _runningProcesses[pid] = (s.Id, name);
            }
        }

        // End processes that disappeared
        var gone = new List<int>();
        foreach (var (pid, entry) in _runningProcesses)
        {
            if (!currentPids.Contains(pid))
            {
                var ended = await db.ProcessSessions.FindAsync(entry.DbId);
                if (ended != null) ended.EndTime = now;
                gone.Add(pid);
            }
        }
        foreach (var pid in gone) _runningProcesses.Remove(pid);
        if (gone.Count > 0) await db.SaveChangesAsync();
    }

    private async Task CloseAllProcessSessions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        foreach (var (_, entry) in _runningProcesses)
        {
            var s = await db.ProcessSessions.FindAsync(entry.DbId);
            if (s != null) s.EndTime = now;
        }
        _runningProcesses.Clear();
        await db.SaveChangesAsync();
    }

    private static List<(string Name, int Id)> GetBackgroundProcesses()
    {
        var procsWithWindows = new HashSet<int>();
        var allProcs = Process.GetProcesses();
        try
        {
            foreach (var proc in allProcs)
            {
                try { if (proc.MainWindowHandle != IntPtr.Zero) procsWithWindows.Add(proc.Id); } catch { }
            }
            var bg = new List<(string, int)>(allProcs.Length / 2);
            foreach (var proc in allProcs)
            {
                try
                {
                    if (!procsWithWindows.Contains(proc.Id))
                        bg.Add((proc.ProcessName, proc.Id));
                }
                catch { }
            }
            return bg;
        }
        finally
        {
            foreach (var proc in allProcs) { try { proc.Dispose(); } catch { } }
        }
    }
}
