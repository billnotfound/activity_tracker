// Sleep/wake detection via heartbeat gap.
//
// Every 30s, updates a single-row Heartbeats table. If the gap exceeds 33s
// (30s + 3s tolerance), the system was asleep or the program was stopped:
//   - Sleep: records Sleep+Wake SystemEvents
//   - Shutdown: records Shutdown+Start SystemEvents (Program.cs writes Shutdown)
//
// Other trackers query SystemEvents for the latest Wake/Start event to detect gaps.
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
        // Ensure tables exist (Heartbeats was created via raw SQL before EF Core model existed)
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS Heartbeats (Id INTEGER PRIMARY KEY, LastTick TEXT NOT NULL)");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS SystemEvents (Id INTEGER PRIMARY KEY AUTOINCREMENT, EventType TEXT NOT NULL, Timestamp TEXT NOT NULL, DurationSeconds REAL NOT NULL DEFAULT 0)");
            // Migration: add DurationSeconds to tables created before this column existed.
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE SystemEvents ADD COLUMN DurationSeconds REAL NOT NULL DEFAULT 0"); }
            catch { /* column already exists */ }
        }

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
                    // First run — seed the heartbeat.
                    db.Heartbeats.Add(new Heartbeat { Id = 1, LastTick = now });
                }
                else if ((now - hb.LastTick).TotalSeconds > GapThresholdSec)
                {
                    await HandleGapAsync(db, hb.LastTick, now);
                }

                // Always update heartbeat so the next iteration can measure the gap.
                if (hb != null) hb.LastTick = now;

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HeartbeatService error");
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalSec), stoppingToken);
        }
    }

    // Detects whether a heartbeat gap was caused by system sleep or a clean shutdown.
    // On shutdown, Program.cs writes a "Shutdown" SystemEvent before exiting.
    // If that event exists between lastHeartbeat and now, it was a shutdown.
    // Otherwise, it was a sleep (or crash — indistinguishable from sleep).
    private async Task HandleGapAsync(AppDbContext db, DateTime lastHeartbeat, DateTime now)
    {
        var gapSec = (now - lastHeartbeat).TotalSeconds;

        var shutdown = await db.SystemEvents
            .Where(e => e.EventType == "Shutdown" && e.Timestamp > lastHeartbeat && e.Timestamp <= now)
            .OrderBy(e => e.Timestamp)
            .FirstOrDefaultAsync();

        if (shutdown != null)
        {
            _logger.LogInformation("Shutdown detected: off for {Gap}s", gapSec);
            shutdown.DurationSeconds = (now - shutdown.Timestamp).TotalSeconds;
            db.SystemEvents.Add(new SystemEvent
            {
                EventType = "Start",
                Timestamp = now
            });
        }
        else
        {
            _logger.LogInformation("Sleep/wake: {Gap}s gap", gapSec);
            db.SystemEvents.Add(new SystemEvent
            {
                EventType = "Sleep",
                Timestamp = lastHeartbeat,
                DurationSeconds = gapSec
            });
            db.SystemEvents.Add(new SystemEvent
            {
                EventType = "Wake",
                Timestamp = now
            });
        }
    }
}
