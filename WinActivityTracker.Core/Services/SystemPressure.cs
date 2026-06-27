namespace WinActivityTracker.Core.Services;

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

        var fill = _writeQueue.ChannelFillPercent;
        var elapsed = (DateTime.UtcNow - _writeQueue.LastSuccessfulFlush).TotalSeconds;
        var cfg = _settings.Settings;

        if (fill >= cfg.PressureCriticalFillPercent || elapsed >= cfg.PressureCriticalLatencySec)
            return PressureLevel.Critical;
        if (fill >= cfg.PressureElevatedFillPercent || elapsed >= cfg.PressureElevatedLatencySec)
            return PressureLevel.Elevated;
        return PressureLevel.Normal;
    }
}
