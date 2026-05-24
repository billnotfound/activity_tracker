// Tracks background processes — those WITHOUT a visible window.
//
// Classification logic:
//   1. Enumerate ALL running processes via System.Diagnostics.Process.GetProcesses()
//   2. Build a HashSet of PIDs that HAVE a MainWindowHandle != IntPtr.Zero
//   3. Background = all processes MINUS those with windows MINUS excluded processes
//
// This is a coarse heuristic: some GUI apps set MainWindowHandle after the window opens,
// and some console apps set MainWindowHandle for hidden windows. In practice, it reliably
// separates user-facing apps from services/daemons/background utilities.
//
// Special delay: starts 10s after the host to let WindowTracker populate first.
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

        // Pass 1: find all PIDs that have a main window
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                    procsWithWindows.Add(proc.Id);
            }
            catch { }  // Process exited or access denied — skip
        }

        // Pass 2: collect background PIDs, excluding those with windows or on the deny-list
        var background = new List<(string, int)>();
        foreach (var proc in Process.GetProcesses())
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
