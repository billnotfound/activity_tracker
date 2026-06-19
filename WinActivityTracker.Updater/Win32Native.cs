using System.Runtime.InteropServices;

namespace WinActivityTracker.Updater;

internal static class Win32Native
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool MoveFileEx(
        string? lpExistingFileName,
        string? lpNewFileName,
        MoveFileFlags dwFlags);

    [Flags]
    public enum MoveFileFlags : uint
    {
        MOVEFILE_REPLACE_EXISTING = 1,
        MOVEFILE_COPY_ALLOWED = 2,
        MOVEFILE_DELAY_UNTIL_REBOOT = 4,
        MOVEFILE_WRITE_THROUGH = 8,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);
}
