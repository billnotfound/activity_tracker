// Detects user idle state via GetLastInputInfo() — a kernel-level tick counter of the last
// keyboard or mouse event. More reliable than polling cursor position (which fails during
// fullscreen video/games where the cursor is hidden).
//
// Used by WindowTracker: when idle, focus tracking is paused so AFK time doesn't inflate
// an application's focus duration. When the user returns, a new focus session begins.
//
// Idle threshold is configurable via settings (IdleThresholdMinutes).
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
        var idleMs = Environment.TickCount - (int)info.dwTime;
        var thresholdMs = _settings.Settings.IdleThresholdMinutes * 60_000;
        IsIdle = idleMs > thresholdMs;
        return IsIdle;
    }
}
