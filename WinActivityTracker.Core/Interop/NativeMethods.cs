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

    // Returns true if the window is minimized — excluded from visible-window enumeration.
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    // Retrieves the show state (normal, maximized, minimized) of a window.
    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    // Retrieves the bounding rectangle of a window (screen coordinates).
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // Window show commands from GetWindowPlacement.showCmd
    public const uint SW_SHOWMAXIMIZED = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint length;
        public uint flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left; public int top; public int right; public int bottom;
        public int Width => right - left;
        public int Height => bottom - top;
    }

    // Gets the monitor handle for a window, then retrieves its info for screen bounds.
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // cbSize must be set to sizeof(LASTINPUTINFO) before calling GetLastInputInfo.
    // dwTime receives the tick count of the last input event.
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    // ===== Process enumeration via CreateToolhelp32Snapshot =====
    // Avoids Process.GetProcesses() — no managed Process objects, no kernel handles.

    // QueryFullProcessImageName with PROCESS_QUERY_LIMITED_INFORMATION resolves
    // PID → full .exe path across integrity levels (e.g., non-admin → admin process).
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    public const uint TH32CS_SNAPPROCESS = 0x00000002;
    public const uint TH32CS_SNAPNOHEAPS = 0x40000000;

    [DllImport("kernel32.dll")]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
