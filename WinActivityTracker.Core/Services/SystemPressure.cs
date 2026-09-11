namespace WinActivityTracker.Core.Services;

using WinActivityTracker.Core.Models;

public enum PressureLevel
{
    Normal = 0,
    Elevated = 1,
    Critical = 2
}

public class SystemPressure
{
    private readonly WriteQueue _writeQueue;
    private readonly SettingsService _settings;

    public SystemPressure(WriteQueue writeQueue, SettingsService settings)
    {
        _writeQueue = writeQueue;
        _settings = settings;
    }

    public PressureLevel GetCurrent()
    {
        if (!_settings.Settings.PressureThrottlingEnabled)
            return PressureLevel.Normal;

        return Evaluate(
            _writeQueue.ChannelFillPercent,
            _writeQueue.PendingCount,
            (DateTime.UtcNow - _writeQueue.LastSuccessfulFlush).TotalSeconds,
            _settings.Settings);
    }

    /// <summary>
    /// 纯判定逻辑，可单测。空闲队列（pendingCount==0）不刷盘，elapsed 持续增长表示
    /// "无待处理工作"而非 IO 阻塞——只有队列非空时 elapsed 才代表刷盘延迟。
    /// </summary>
    public static PressureLevel Evaluate(int fill, int pendingCount, double elapsedSec, TrackerSettings cfg)
    {
        var hasPendingWork = pendingCount > 0;

        if (fill >= cfg.PressureCriticalFillPercent
            || (hasPendingWork && elapsedSec >= cfg.PressureCriticalLatencySec))
            return PressureLevel.Critical;
        if (fill >= cfg.PressureElevatedFillPercent
            || (hasPendingWork && elapsedSec >= cfg.PressureElevatedLatencySec))
            return PressureLevel.Elevated;
        return PressureLevel.Normal;
    }
}
