// Writes a heartbeat timestamp to the DB every 30s.
// On startup, checks the gap between now and the last heartbeat.
// If gap > 33s, inserts a __SystemSleep marker for the missing period.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Trackers;

public class HeartbeatService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatService> _logger;
    private const int IntervalSec = 30;
    private const int GapThresholdSec = 33; // 30s + 3s tolerance

    public HeartbeatService(IServiceScopeFactory scopeFactory, ILogger<HeartbeatService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure the Heartbeats table exists (DB may have been created before this table was added).
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS Heartbeats (Id INTEGER PRIMARY KEY, LastTick TEXT NOT NULL)");
        }

        // On startup, check if we missed time (program was stopped or system slept).
        await CheckStartupGap();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;

                var hb = await db.Heartbeats.FindAsync(1);
                if (hb == null)
                {
                    db.Heartbeats.Add(new Heartbeat { Id = 1, LastTick = now });
                }
                else
                {
                    hb.LastTick = now;
                }
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HeartbeatService error");
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalSec), stoppingToken);
        }
    }

    private async Task CheckStartupGap()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var last = await db.Heartbeats.FindAsync(1);

            if (last != null && (now - last.LastTick).TotalSeconds > GapThresholdSec)
            {
                _logger.LogInformation("Startup gap detected: {Gap}s — inserting SystemSleep marker",
                    (now - last.LastTick).TotalSeconds);

                db.FocusChanges.Add(new FocusChange
                {
                    Timestamp = last.LastTick,
                    ProcessName = "__SystemSleep",
                    WindowTitle = "Program stopped or system slept",
                    DurationSeconds = (now - last.LastTick).TotalSeconds
                });
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat startup gap check error");
        }
    }
}
