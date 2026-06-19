using System.Diagnostics;
using System.ComponentModel;
using System.ServiceProcess;

namespace WinActivityTracker.Updater;

/// <summary>
/// Locates the old version's executable path.
/// 3-step search: PID file → auto-start registry → failure prompt.
/// Supports test mode isolation via env var.
/// </summary>
internal static class OldVersionLocator
{
    private const string PidFileName = "taskmonitor114.pid";
    private const string TestPidFileName = "taskmonitor114-test.pid";

    private static string EffectivePidFileName =>
        RegistryHelper.IsTestMode ? TestPidFileName : PidFileName;

    /// <summary>
    /// 3-step search. Returns the full path to old taskmonitor114.exe, or null.
    /// </summary>
    public static string? FindOldExePath()
    {
        // Step 1: PID file → Process.MainModule.FileName
        var exePath = FindViaPidFile();
        if (exePath != null) return exePath;

        // Step 2: Auto-start registry
        exePath = RegistryHelper.ReadAutoStartExePath();
        if (exePath != null && File.Exists(exePath)) return exePath;

        return null;
    }

    private static string? FindViaPidFile()
    {
        var dataDir = RegistryHelper.ReadAppRegistry("DataDir");
        if (string.IsNullOrWhiteSpace(dataDir)) return null;

        var pidFile = Path.Combine(dataDir, EffectivePidFileName);
        if (!File.Exists(pidFile)) return null;

        try
        {
            var text = File.ReadAllText(pidFile).Trim();
            if (!int.TryParse(text, out var pid)) return null;

            try
            {
                using var process = Process.GetProcessById(pid);

                // Verify it's actually our process
                if (!process.ProcessName.Equals("taskmonitor114", StringComparison.OrdinalIgnoreCase))
                    return null;

                // Check same session (avoid cross-session PID reuse)
                if (Win32Native.ProcessIdToSessionId((uint)pid, out var sessionId))
                {
                    var mySession = Process.GetCurrentProcess().SessionId;
                    if (sessionId != mySession)
                    {
                        Console.WriteLine($"PID {pid} 在不同会话 (session {sessionId} vs {mySession})，跳过。");
                        return null;
                    }
                }

                return process.MainModule?.FileName;
            }
            catch (ArgumentException)
            {
                // Process no longer exists — stale PID file
            }
            catch (Win32Exception)
            {
                // Access denied (elevated process?)
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"读取 PID 文件失败: {ex.Message}");
        }
        return null;
    }
}
