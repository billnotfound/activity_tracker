// Detects user idle via GetLastInputInfo(), a kernel-level tick counter for the last
// keyboard/mouse event.
//
// Used by WindowTracker: when idle, focus tracking is paused so AFK time is not counted.
// Idle threshold is set via TrackerSettings.IdleThresholdMinutes.
using System.Runtime.InteropServices;
using WinActivityTracker.Core.Interop;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Core.Trackers;

public class IdleDetector
{
    private readonly SettingsService _settings;

    public bool IsIdle { get; private set; }

    public IdleDetector(SettingsService settings)
    {
        _settings = settings;
    }

    public bool CheckIdle()
    {
        var info = new NativeMethods.LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);

        if (!NativeMethods.GetLastInputInfo(ref info))
            return IsIdle;

        // TickCount64 avoids the 24.9-day wrap-around of the 32-bit TickCount.
        var idleMs = Environment.TickCount64 - info.dwTime;
        var thresholdMs = _settings.Settings.IdleThresholdMinutes * 60_000L;
        IsIdle = idleMs > thresholdMs;
        return IsIdle;
    }
}
