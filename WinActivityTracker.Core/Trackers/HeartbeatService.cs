using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Interop;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Core.Trackers;

public class HeartbeatService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WriteQueue _writeQueue;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly Action<TimeAnomaly>? _onAnomalyDetected;
    private readonly SettingsService _settings;
    private readonly DateTime _bootWallTime;
    private const int IntervalSec = 30;
    private const int GapThresholdSec = 33;

    public HeartbeatService(IServiceScopeFactory scopeFactory, WriteQueue writeQueue,
        ILogger<HeartbeatService> logger, SettingsService settings,
        Action<TimeAnomaly>? onAnomalyDetected = null)
    {
        _scopeFactory = scopeFactory;
        _writeQueue = writeQueue;
        _logger = logger;
        _settings = settings;
        _onAnomalyDetected = onAnomalyDetected;
        // 运行期内不变的 boot 墙钟起点：boot 时间 = 当前墙钟 − tick。
        _bootWallTime = DateTime.UtcNow - TimeSpan.FromMilliseconds(MonotonicClock.NowMs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeAnomaly? detected = null;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;
                var nowTick = MonotonicClock.NowMs;
                var threshold = _settings.Settings.TimeAnomalyThresholdSeconds;

                var hb = await db.Heartbeats.FindAsync(1);

                if (hb == null)
                {
                    db.Heartbeats.Add(new Heartbeat
                    {
                        Id = 1,
                        LastTick = now,
                        LastMonotonicMs = nowTick,
                        BootWallTime = _bootWallTime
                    });
                }
                else
                {
                    var wallDelta = (now - hb.LastTick).TotalSeconds;

                    if (!IsSameBoot(hb.BootWallTime, _bootWallTime))
                    {
                        // 跨重启/升级旧数据：tick 跨 boot 归零不可比，豁免异常检测；
                        // 但墙钟 gap 的睡眠/关机检测必须保留（HandleGapAsync 是 Start 事件的唯一写入者）
                        if (wallDelta > GapThresholdSec)
                            await HandleGapAsync(db, hb.LastTick, now);
                    }
                    else
                    {
                        var tickDelta = (nowTick - hb.LastMonotonicMs) / 1000.0;
                        var cls = TimeAnomalyClassifier.Classify(tickDelta, wallDelta,
                            ntp: null, threshold, _settings.Settings.NtpEpsilonSeconds);

                        switch (cls.Kind)
                        {
                            case TimeAnomalyKind.ForwardJump:
                            case TimeAnomalyKind.BackwardJump:
                                // 时间被改：写异常，不写 Sleep（旧逻辑会误判为睡眠）
                                detected = RecordAnomaly(db, hb, cls, now);
                                break;
                            case TimeAnomalyKind.SuspiciousHourOffset:
                            case TimeAnomalyKind.Drift:
                                detected = RecordSilent(db, hb, cls, now);
                                break;
                            default:
                                if (wallDelta > GapThresholdSec)
                                    await HandleGapAsync(db, hb.LastTick, now);  // 现有睡眠/关机逻辑
                                break;
                        }
                    }

                    hb.LastTick = now;
                    hb.LastMonotonicMs = nowTick;
                    hb.BootWallTime = _bootWallTime;
                }

                await db.SaveChangesAsync();

                // 回调在 SaveChanges 之后：收到的是已保存实体（Id 已赋值，NTP 确认可按 Id 查库）。
                // 异常只记日志、不向外抛——否则心跳循环会被中止，每 30s 产生重复异常行。
                if (detected != null)
                {
                    try { _onAnomalyDetected?.Invoke(detected); }
                    catch (Exception ex) { _logger.LogError(ex, "onAnomalyDetected callback failed"); }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HeartbeatService error");
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalSec), stoppingToken);
        }
    }

    /// <summary>
    /// 判断持久化的 boot 墙钟起点是否属于当前 boot。tick 跨重启归零，
    /// 只有同 boot 的 tick 差才可比；旧数据（BootWallTime=default）视为不同 boot。
    /// </summary>
    public static bool IsSameBoot(DateTime storedBootWall, DateTime currentBootWall)
        => storedBootWall != default && Math.Abs((storedBootWall - currentBootWall).TotalSeconds) < 2;

    private TimeAnomaly RecordAnomaly(AppDbContext db, Heartbeat hb, TimeAnomalyClassification cls, DateTime now)
    {
        _logger.LogWarning("Time change detected: offset {Offset}s at {At}", cls.OffsetSeconds, now);
        var anomaly = new TimeAnomaly
        {
            DetectedAt = now,
            Source = TimeAnomalySources.Heartbeat,
            OffsetSeconds = cls.OffsetSeconds,
            Status = TimeAnomalyStatus.Confirmed,
            FromWall = hb.LastTick.AddSeconds(Math.Max(0, cls.OffsetSeconds)),
            Note = cls.Note
        };
        // 直接写本作用域 DbContext（每 30s 至多一行，压力可忽略）：回调在
        // SaveChangesAsync 之后触发，必须收到已保存实体（Id 已赋值）。
        db.TimeAnomalies.Add(anomaly);
        return anomaly;
    }

    private TimeAnomaly RecordSilent(AppDbContext db, Heartbeat hb, TimeAnomalyClassification cls, DateTime now)
    {
        _logger.LogInformation("Time anomaly (silent): {Kind}, offset {Offset}s", cls.Kind, cls.OffsetSeconds);
        var anomaly = new TimeAnomaly
        {
            DetectedAt = now,
            Source = TimeAnomalySources.Heartbeat,
            OffsetSeconds = cls.OffsetSeconds,
            Status = cls.Kind == TimeAnomalyKind.Drift
                ? TimeAnomalyStatus.Drift
                : TimeAnomalyStatus.Suspicious,
            FromWall = hb.LastTick,
            Note = cls.Note
        };
        // Suspicious/Drift 同样回调（TimeAnomalyService 对 Drift 是 no-op，
        // Suspicious 触发 NTP 确认）；同样必须收到已保存实体。
        db.TimeAnomalies.Add(anomaly);
        return anomaly;
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
            // Route gap event writes through WriteQueue to avoid adding I/O
            // pressure to the heartbeat's own SaveChangesAsync.
            var sdId = shutdown.Id;
            var sdDuration = (now - shutdown.Timestamp).TotalSeconds;
            _writeQueue.TryWrite(wdb =>
            {
                var s = wdb.SystemEvents.Find(sdId);
                if (s != null) s.DurationSeconds = sdDuration;
                wdb.SystemEvents.Add(new SystemEvent
                {
                    EventType = SystemEventTypes.Start,
                    Timestamp = now
                });
            });
        }
        else
        {
            _logger.LogInformation("Sleep/wake: {Gap}s gap", gapSec);
            _writeQueue.TryWrite(wdb =>
            {
                wdb.SystemEvents.Add(new SystemEvent
                {
                    EventType = SystemEventTypes.Sleep,
                    Timestamp = lastHeartbeat,
                    DurationSeconds = gapSec
                });
                wdb.SystemEvents.Add(new SystemEvent
                {
                    EventType = SystemEventTypes.Wake,
                    Timestamp = now
                });
            });
        }
    }
}
