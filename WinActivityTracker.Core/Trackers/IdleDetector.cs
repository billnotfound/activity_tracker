using System.Runtime.InteropServices;
using WinActivityTracker.Core.Interop;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Core.Trackers;

public class IdleDetector
{
    private readonly SettingsService _settings;

    public bool IsIdle { get; private set; }

    /// <summary>
    /// Set by MediaSessionTracker each poll cycle. When true, idle detection is
    /// bypassed — the user is listening to music / watching video regardless of
    /// keyboard/mouse activity.
    /// </summary>
    public bool IsMediaPlaying { get; set; }

    public IdleDetector(SettingsService settings)
    {
        _settings = settings;
    }

    public bool CheckIdle()
    {
        if (IsMediaPlaying)
        {
            IsIdle = false;
            return false;
        }

        var info = new NativeMethods.LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);

        if (!NativeMethods.GetLastInputInfo(ref info))
            return IsIdle;

        var idleMs = Environment.TickCount64 - info.dwTime;
        var thresholdMs = _settings.Settings.IdleThresholdMinutes * 60_000L;
        IsIdle = idleMs > thresholdMs;
        return IsIdle;
    }
}
