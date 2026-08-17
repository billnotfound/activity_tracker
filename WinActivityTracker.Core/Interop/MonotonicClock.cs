using System.Runtime.InteropServices;

namespace WinActivityTracker.Core.Interop;

/// <summary>
/// 单调时钟：系统启动以来的毫秒数（GetTickCount64）。
/// 包含睡眠/休眠时长，不受 SetSystemTime 等墙钟修改影响。
/// UInt64 毫秒约 5.8 亿年才回绕，实际不会发生 —— 调用方仍须用 UInt64 减法（unchecked 自动处理）。
/// </summary>
public static class MonotonicClock
{
    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    public static ulong NowMs => GetTickCount64();
}
