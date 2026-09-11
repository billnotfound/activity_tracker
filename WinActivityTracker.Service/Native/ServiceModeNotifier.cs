// 服务模式通知（会话 0 / LocalSystem）。
//
// 服务进程运行在会话 0，无交互桌面，不能直接弹 toast。本实现用
// WTSGetActiveConsoleSessionId + WTSQueryUserToken + CreateEnvironmentBlock +
// CreateProcessAsUser 把同 exe 的 --toast-helper 分支投递到活动交互会话；
// helper 在用户桌面弹 toast、处理点击。
//
// 骨架级实现（P/Invoke）：无交互会话 / 非 LocalSystem / 启动失败 → 只写日志，
// 不中断检测主流程。
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

/// <summary>
/// 服务模式 ITimeAnomalyNotifier：把确认异常投递到交互会话的 --toast-helper 进程。
/// CancelReminder 为空操作（helper 只弹 2 次提醒，服务侧不追踪排程句柄）。
/// </summary>
public sealed class ServiceModeNotifier : ITimeAnomalyNotifier
{
    private readonly ILogger<ServiceModeNotifier> _logger;
    private readonly SettingsService _settings;

    public ServiceModeNotifier(ILogger<ServiceModeNotifier> logger, SettingsService settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public void NotifyConfirmed(TimeAnomaly anomaly)
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                _logger.LogInformation("No interactive console session; skipping toast for anomaly {Id}", anomaly.Id);
                return;
            }

            if (!WTSQueryUserToken(sessionId, out var token) || token == IntPtr.Zero)
            {
                _logger.LogInformation("WTSQueryUserToken failed for session {Session} (win32 {Code}); " +
                    "service not running as LocalSystem?", sessionId, Marshal.GetLastWin32Error());
                return;
            }

            try
            {
                if (!CreateEnvironmentBlock(out var env, token, false))
                {
                    _logger.LogInformation("CreateEnvironmentBlock failed (win32 {Code}); continuing with null env",
                        Marshal.GetLastWin32Error());
                    env = IntPtr.Zero;
                }

                var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "taskmonitor114.exe");
                // helper 在用户桌面侧，无法解析服务侧（LocalSystem）的 DB/设置路径——
                // 异常 ID / 偏移 / API 端口全部走命令行，helper 只消费参数。
                var commandLine = string.Create(CultureInfo.InvariantCulture,
                    $"\"{exe}\" --toast-helper {anomaly.Id} --offset {anomaly.OffsetSeconds:F0} --port {_settings.Settings.ApiPort}");

                var si = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop = @"winsta0\default"
                };
                if (!CreateProcessAsUser(token, null, commandLine,
                        IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT,
                        env, null, ref si, out var pi))
                {
                    _logger.LogWarning("CreateProcessAsUser failed for anomaly {Id} (win32 {Code}): {Msg}",
                        anomaly.Id, Marshal.GetLastWin32Error(),
                        new Win32Exception(Marshal.GetLastWin32Error()).Message);
                }
                else
                {
                    _logger.LogInformation("Dispatched toast-helper for anomaly {Id} to session {Session}",
                        anomaly.Id, sessionId);
                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);
                }

                if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service toast delivery failed for anomaly {Id}", anomaly.Id);
        }
    }

    public void CancelReminder(long anomalyId)
    {
        // 服务模式暂不取消（2 次提醒可接受）；helper 生命周期自管
    }

    // ===== P/Invoke 骨架 =====

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken,
        [MarshalAs(UnmanagedType.Bool)] bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}
