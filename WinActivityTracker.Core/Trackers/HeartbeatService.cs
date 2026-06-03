// Heartbeat — sole source of truth for sleep/wake detection.
//
// Every 30s, updates a single-row Heartbeats table with the current UTC time.
// If the gap between now and the previous heartbeat exceeds 33s (30s + 3s tolerance),
// the system was asleep or the program was stopped. In that case:
//
//   1. SystemEvent "Sleep" is recorded at the last heartbeat time.
//   2. SystemEvent "Wake" is recorded at the current time.
//   3. A __SystemSleep FocusChange covers the gap (backward compatible with existing queries).
//
// Other trackers (WindowTracker, MediaSessionTracker) query SystemEvents for the
// latest Wake event instead of doing their own ad-hoc gap detection.
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
        // Ensure tables exist — Heartbeats is a single-row table; SystemEvents
        // records every sleep/wake pair with sleep duration. Both tables may predate
        // their EF Core model definitions, so raw SQL ensures they exist.
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
                    // System slept or program was stopped.
                    RecordSleepWake(db, hb.LastTick, now);
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

    private void RecordSleepWake(AppDbContext db, DateTime sleepTime, DateTime wakeTime)
    {
        var gapSec = (wakeTime - sleepTime).TotalSeconds;
        _logger.LogInformation("Sleep/wake: {Gap}s gap — recording events", gapSec);

        db.SystemEvents.Add(new SystemEvent
        {
            EventType = "Sleep",
            Timestamp = sleepTime,
            DurationSeconds = gapSec
        });
        db.SystemEvents.Add(new SystemEvent
        {
            EventType = "Wake",
            Timestamp = wakeTime
        });

        // __SystemSleep FocusChange is no longer inserted here.
        // Sleep time is now queried directly from SystemEvents (SUM DurationSeconds
        // WHERE EventType = 'Sleep'), which is more accurate and avoids polluting
        // the focus-change timeline with synthetic records.
    }
}
