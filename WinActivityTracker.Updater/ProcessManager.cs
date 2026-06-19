using System.Diagnostics;
using System.ServiceProcess;

namespace WinActivityTracker.Updater;

/// <summary>
/// Kills the old process and starts the new one.
/// Supports Windows Service mode and test mode.
/// </summary>
internal static class ProcessManager
{
    /// <summary>
    /// Attempt graceful close, then force kill. Returns true if process is gone.
    /// Also handles Windows Service mode.
    /// </summary>
    public static bool KillOldProcess(string oldExePath)
    {
        if (RegistryHelper.IsTestMode)
        {
            Console.WriteLine($"[TEST] Would kill process at: {oldExePath}");
            return true;
        }

        // 1. Check if running as Windows Service
        if (TryStopService("taskmonitor114"))
            return true;

        // 2. Find process by name, then verify path
        var processes = Process.GetProcessesByName("taskmonitor114");
        if (processes.Length == 0) return true;

        foreach (var process in processes)
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, oldExePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                Console.WriteLine($"正在停止旧进程 (PID {process.Id})...");

                // Graceful close first (sends WM_CLOSE via WinForms)
                process.CloseMainWindow();
                if (process.WaitForExit(5000))
                {
                    Console.WriteLine("旧进程已优雅退出。");
                    continue;
                }

                // Force kill with entire process tree
                Console.WriteLine("正在强制终止...");
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(3000))
                {
                    Console.Error.WriteLine($"无法终止进程 PID {process.Id}");
                    return false;
                }
                Console.WriteLine("旧进程已终止。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"终止进程出错: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
        return true;
    }

    /// <summary>
    /// Start the new version from the given exe path.
    /// </summary>
    public static void StartNewVersion(string exePath)
    {
        if (RegistryHelper.IsTestMode)
        {
            Console.WriteLine($"[TEST] Would start: {exePath}");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--autostart",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!
        };
        Process.Start(startInfo);
        Console.WriteLine($"已启动新版本: {exePath}");
    }

    private static bool TryStopService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Stopped)
                return true;

            Console.WriteLine($"正在停止 Windows 服务 {serviceName}...");
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            return true;
        }
        catch (InvalidOperationException)
        {
            // Service doesn't exist — not running as service
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"停止服务失败: {ex.Message}");
            return false;
        }
    }
}
