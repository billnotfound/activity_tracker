// Tracks background processes — those WITHOUT a visible window.
//
// Classification (single GetProcesses() call for efficiency):
//   1. Get all running processes once, store in local array.
//   2. Pass 1: build HashSet of PIDs with MainWindowHandle != IntPtr.Zero.
//   3. Pass 2: collect PIDs without windows and not in the exclusion list.
//   4. Dispose all Process objects in a finally block to release native handles immediately.
//
// Heuristic: some GUI apps set MainWindowHandle late; some console apps set it
// for hidden windows. In practice it reliably separates user-facing from background.
//
// Starts 10s after the host to let WindowTracker's first poll complete.
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

    public ProcessTracker(IServiceScopeFactory scopeFactory, SettingsService settings, ILogger<ProcessTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcessTracker started, interval: {Interval}s", _settings.Settings.ProcessPollSeconds);

        // Delay initial enumeration to avoid racing with the WindowTracker's first poll
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

                var backgroundProcs = GetBackgroundProcesses();
                await SaveSnapshot(backgroundProcs);
                _logger.LogDebug("Background processes: {Count}", backgroundProcs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessTracker poll error");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.ProcessPollSeconds), stoppingToken);
        }
    }

    private List<(string Name, int Id)> GetBackgroundProcesses()
    {
        var excluded = _settings.Settings.ExcludedProcesses;
        var procsWithWindows = new HashSet<int>();

        // Single call to GetProcesses() — was two calls before, allocating ~800 Process objects/cycle.
        var allProcs = Process.GetProcesses();
        try
        {
            foreach (var proc in allProcs)
            {
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                        procsWithWindows.Add(proc.Id);
                }
                catch { }
            }

            var background = new List<(string, int)>(allProcs.Length / 2);
            foreach (var proc in allProcs)
            {
                try
                {
                    if (excluded.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase))
                        continue;
                    if (!procsWithWindows.Contains(proc.Id))
                        background.Add((proc.ProcessName, proc.Id));
                }
                catch { }
            }
            return background;
        }
        finally
        {
            // Dispose all Process objects to release native handles immediately.
            // Without this, GC collects them eventually but memory stays high.
            foreach (var proc in allProcs)
            {
                try { proc.Dispose(); } catch { }
            }
        }
    }

    private async Task SaveSnapshot(List<(string Name, int Id)> procs)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        foreach (var (name, id) in procs)
        {
            db.ProcessSnapshots.Add(new ProcessSnapshot
            {
                Timestamp = now,
                ProcessName = name,
                ProcessId = id
            });
        }

        await db.SaveChangesAsync();
    }
}
