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
    private const int GapThresholdSec = 33;

    public HeartbeatService(IServiceScopeFactory scopeFactory, ILogger<HeartbeatService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                else if ((now - hb.LastTick).TotalSeconds > GapThresholdSec)
                {
                    await HandleGapAsync(db, hb.LastTick, now);
                }

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

    private async Task HandleGapAsync(AppDbContext db, DateTime lastHeartbeat, DateTime now)
    {
        var gapSec = (now - lastHeartbeat).TotalSeconds;

        var shutdown = await db.SystemEvents
            .Where(e => e.EventType == SystemEventTypes.Shutdown
                && e.Timestamp > lastHeartbeat && e.Timestamp <= now)
            .OrderBy(e => e.Timestamp)
            .FirstOrDefaultAsync();

        if (shutdown != null)
        {
            _logger.LogInformation("Shutdown detected: off for {Gap}s", gapSec);
            shutdown.DurationSeconds = (now - shutdown.Timestamp).TotalSeconds;
            db.SystemEvents.Add(new SystemEvent
            {
                EventType = SystemEventTypes.Start,
                Timestamp = now
            });
        }
        else
        {
            _logger.LogInformation("Sleep/wake: {Gap}s gap", gapSec);
            db.SystemEvents.Add(new SystemEvent
            {
                EventType = SystemEventTypes.Sleep,
                Timestamp = lastHeartbeat,
                DurationSeconds = gapSec
            });
            db.SystemEvents.Add(new SystemEvent
            {
                EventType = SystemEventTypes.Wake,
                Timestamp = now
            });
        }
    }
}
