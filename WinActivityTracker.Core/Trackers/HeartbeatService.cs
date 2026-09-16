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
    private readonly ILogger<HeartbeatService> _logger;
    private readonly Action<TimeAnomaly>? _onAnomalyDetected;
    private readonly SettingsService _settings;
    private readonly SystemPowerEventReader _powerEvents;
    private readonly DateTime _bootWallTime;
    private DateTime? _pendingPowerLogSince;
    private int _pendingPowerLogScans;
    private const int IntervalSec = 30;
    // Leave two full heartbeat intervals of scheduling tolerance. A 33-second
    // threshold classified routine DB/CPU stalls as system sleep.
    private const int GapThresholdSec = IntervalSec * 3;

    public HeartbeatService(IServiceScopeFactory scopeFactory,
        ILogger<HeartbeatService> logger, SettingsService settings,
        SystemPowerEventReader powerEvents,
        Action<TimeAnomaly>? onAnomalyDetected = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings;
        _powerEvents = powerEvents;
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

                if (_pendingPowerLogSince.HasValue)
                    await RetryPendingPowerLogAsync(db, now);

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

                    var sameBoot = IsSameBoot(hb.BootWallTime, _bootWallTime);
                    if (!sameBoot)
                    {
                        // 跨重启/升级旧数据：tick 跨 boot 归零不可比，豁免异常检测；
                        // 但墙钟 gap 的睡眠/关机检测必须保留（HandleGapAsync 是 Start 事件的唯一写入者）
                        if (ShouldRecordGap(0, wallDelta, sameBoot))
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
                                // 时钟变化单独记异常；是否睡眠由 tick 间隔判定。
                                detected = RecordAnomaly(db, hb, cls, now);
                                if (ShouldRecordGap(tickDelta, wallDelta, sameBoot))
                                    await HandleGapAsync(db, hb.LastTick, now);
                                break;
                            case TimeAnomalyKind.SuspiciousHourOffset:
                            case TimeAnomalyKind.Drift:
                                detected = RecordSilent(db, hb, cls, now);
                                if (ShouldRecordGap(tickDelta, wallDelta, sameBoot))
                                    await HandleGapAsync(db, hb.LastTick, now);
                                break;
                            default:
                                if (ShouldRecordGap(tickDelta, wallDelta, sameBoot))
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

    public static bool ShouldRecordGap(double tickDelta, double wallDelta, bool sameBoot)
    {
        if (!sameBoot) return wallDelta > GapThresholdSec;
        return tickDelta > GapThresholdSec;
    }

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
            FromWall = hb.LastTick.AddSeconds(Math.Max(0, cls.OffsetSeconds)),
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

        // Windows logs the exact SleepTime/WakeTime (Power-Troubleshooter 1)
        // and shutdown/start boundaries (Kernel-General 13 + EventLog 6005).
        // If the log is readable, it is authoritative: an empty result means a
        // delayed service iteration, not sleep. Keep a short retry window because
        // the resume event can be published a few seconds after processes resume.
        var scanFrom = lastHeartbeat.AddMinutes(-2);
        var powerLog = _powerEvents.GetOffPeriods(scanFrom, now);
        if (powerLog.Succeeded
            && await ImportPowerPeriodsAsync(db, powerLog.Periods, lastHeartbeat, now))
            return;

        var shutdown = await db.SystemEvents
            .Where(e => e.EventType == SystemEventTypes.Shutdown
                && e.Timestamp > lastHeartbeat && e.Timestamp <= now)
            .OrderBy(e => e.Timestamp)
            .FirstOrDefaultAsync();

        if (shutdown != null)
        {
            _logger.LogInformation("Shutdown detected: off for {Gap}s", gapSec);
            // Keep the cursor update and its matching event changes in the same
            // SaveChanges transaction. Previously these were queued separately:
            // when the heartbeat update lost a DB lock race, the event survived
            // but the cursor stayed stale, producing another pair every cycle.
            shutdown.DurationSeconds = (now - shutdown.Timestamp).TotalSeconds;
            db.SystemEvents.Add(new SystemEvent
            {
                EventType = SystemEventTypes.Start,
                Timestamp = now
            });
            return;
        }

        if (powerLog.Succeeded)
        {
            _pendingPowerLogSince = scanFrom;
            _pendingPowerLogScans = 0;
            _logger.LogDebug("Long heartbeat interval had no power event; waiting for event-log reconciliation");
            return;
        }

        // Event log unavailable: preserve the prior monotonic-gap fallback.
        _logger.LogInformation("Sleep/wake fallback: {Gap}s gap", gapSec);
        var alreadyRecorded = await db.SystemEvents.AsNoTracking().AnyAsync(e =>
            e.EventType == SystemEventTypes.Sleep && e.Timestamp == lastHeartbeat);
        if (alreadyRecorded) return;
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

    private async Task RetryPendingPowerLogAsync(AppDbContext db, DateTime now)
    {
        var since = _pendingPowerLogSince!.Value;
        var result = _powerEvents.GetOffPeriods(since, now);
        _pendingPowerLogScans++;
        if (result.Succeeded && await ImportPowerPeriodsAsync(db, result.Periods, since, now))
        {
            _pendingPowerLogSince = null;
            _pendingPowerLogScans = 0;
        }
        else if (_pendingPowerLogScans >= 4)
        {
            _logger.LogDebug("No system power event appeared after reconciliation window; treating interval as scheduler/DB delay");
            _pendingPowerLogSince = null;
            _pendingPowerLogScans = 0;
        }
    }

    private static async Task<bool> ImportPowerPeriodsAsync(
        AppDbContext db,
        IReadOnlyList<SystemOffPeriod> periods,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var matching = periods
            .Where(period => period.End > rangeStart && period.Start < rangeEnd)
            .ToList();
        if (matching.Count == 0) return false;

        foreach (var period in matching)
        {
            var endType = period.EventType == SystemEventTypes.Sleep
                ? SystemEventTypes.Wake
                : SystemEventTypes.Start;
            var toleranceStart = period.Start.AddMinutes(-2);
            var toleranceEnd = period.End.AddMinutes(2);
            var nearby = await db.SystemEvents
                .Where(e => (e.EventType == period.EventType || e.EventType == endType)
                    && e.Timestamp >= toleranceStart && e.Timestamp <= toleranceEnd)
                .ToListAsync();

            var off = nearby
                .Where(e => e.EventType == period.EventType)
                .OrderBy(e => Math.Abs((e.Timestamp - period.Start).TotalSeconds))
                .FirstOrDefault();
            if (off == null)
            {
                off = new SystemEvent { EventType = period.EventType };
                db.SystemEvents.Add(off);
            }
            off.Timestamp = period.Start;
            off.DurationSeconds = (period.End - period.Start).TotalSeconds;

            var end = nearby
                .Where(e => e.EventType == endType)
                .OrderBy(e => Math.Abs((e.Timestamp - period.End).TotalSeconds))
                .FirstOrDefault();
            if (end == null)
            {
                end = new SystemEvent { EventType = endType };
                db.SystemEvents.Add(end);
            }
            end.Timestamp = period.End;
            end.DurationSeconds = 0;
        }
        return true;
    }
}
