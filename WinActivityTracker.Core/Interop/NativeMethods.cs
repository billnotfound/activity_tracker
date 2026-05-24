// Win32 API P/Invoke declarations for window enumeration and input detection.
// All functions come from user32.dll — no external dependencies beyond the Windows OS.
using System.Runtime.InteropServices;
using System.Text;

namespace WinActivityTracker.Core.Interop;

public static class NativeMethods
{
    // Callback signature for EnumWindows — returns false to stop enumeration early.
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Returns the HWND of the window currently receiving keyboard input.
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // Retrieves the process ID that created the given window handle.
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // Copies the window title text into the StringBuilder.
    // CharSet.Auto ensures correct encoding on both ANSI and Unicode systems.
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    // Returns the length (in chars) of the window title — used to allocate the right buffer size.
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    // Filters out hidden windows during enumeration.
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    // Enumerates all top-level windows, calling lpEnumFunc for each.
    // MarshalAs(UnmanagedType.Bool) prevents incorrect bool marshaling on x64.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // Returns the tick count of the last user input event (keyboard or mouse).
    // Used by IdleDetector to pause tracking when the user is AFK.
    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // Returns true if the window is minimized — minimized windows are excluded from visible-window snapshots.
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    // cbSize must be set to sizeof(LASTINPUTINFO) before calling GetLastInputInfo.
    // dwTime receives the tick count of the last input event.
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
