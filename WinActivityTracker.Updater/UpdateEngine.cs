namespace WinActivityTracker.Updater;

/// <summary>
/// Orchestrates the full update cycle: locate old version, kill process,
/// sync files, update registry, start new version, self-cleanup.
/// </summary>
internal class UpdateEngine
{
    private readonly string _newDirectory;
    private readonly ResourceManifest _newManifest;

    public UpdateEngine()
    {
        _newDirectory = AppContext.BaseDirectory;
        _newManifest = ResourceManifest.LoadEmbedded();
    }

    /// <summary>
    /// Run the full update cycle. Returns exit code: 0 = success, 1 = cancelled, 2 = error.
    /// </summary>
    public int Run()
    {
        Console.WriteLine("============================================");
        Console.WriteLine("  taskmonitor114 自动更新");
        Console.WriteLine("============================================");
        Console.WriteLine();

        if (RegistryHelper.IsTestMode)
            Console.WriteLine("[测试模式 - 不会影响真实程序]");

        Console.WriteLine("即将进行自动更新…");
        Console.WriteLine();

        // Step 1: Find old version
        var oldExePath = OldVersionLocator.FindOldExePath();

        if (oldExePath == null)
            return HandleNoOldVersion();

        Console.WriteLine($"找到旧版本: {oldExePath}");
        var oldDirectory = Path.GetDirectoryName(oldExePath)!;
        Console.WriteLine($"旧版目录: {oldDirectory}");
        Console.WriteLine($"新版目录: {_newDirectory}");
        Console.WriteLine();

        // Step 2: Kill old process
        Console.WriteLine("正在停止旧版本进程…");
        if (!ProcessManager.KillOldProcess(oldExePath))
        {
            Console.Error.WriteLine("无法停止旧版本进程。");
            var result = MessageBox.Show(
                "无法停止旧版本进程。\n\n是否重试？",
                "自动更新",
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel)
                return 1;

            if (!ProcessManager.KillOldProcess(oldExePath))
            {
                Console.Error.WriteLine("重试后仍无法停止，退出。");
                return 2;
            }
        }
        Console.WriteLine();

        // Step 3: Load old manifest
        var oldManifest = ResourceManifest.LoadFromDisk(oldDirectory);
        if (oldManifest != null)
        {
            Console.WriteLine($"找到旧版清单 ({oldManifest.FileEntries.Count} 个文件)。");
        }
        else
        {
            oldManifest = ResourceManifest.LoadEmbeddedOld();
            if (oldManifest != null)
                Console.WriteLine($"旧版无 resource.txt，使用内置基准清单 ({oldManifest.FileEntries.Count} 个文件)。");
            else
                Console.WriteLine("旧版无 resource.txt 且无内置基准清单，仅执行复制。");
        }

        // Step 4: Delete removed files
        if (oldManifest != null)
        {
            Console.WriteLine("正在清理已移除的旧文件…");
            FileSynchronizer.DeleteRemovedFiles(oldDirectory, oldManifest, _newManifest);
            Console.WriteLine();
        }

        // Step 5: Ensure directories
        FileSynchronizer.EnsureDirectories(oldDirectory, _newManifest);

        // Step 6: Copy new files
        Console.WriteLine($"正在复制新版本文件 ({_newManifest.FileEntries.Count} 个)...");
        FileSynchronizer.CopyNewFiles(_newDirectory, oldDirectory, _newManifest);
        Console.WriteLine();

        // Step 7: Update auto-start registry (only if path changed)
        var updatedExePath = Path.Combine(oldDirectory, "taskmonitor114.exe");
        RegistryHelper.UpdateAutoStart(updatedExePath);

        // Step 8: Start new version
        Console.WriteLine("正在启动新版本…");
        ProcessManager.StartNewVersion(updatedExePath);

        // Step 9: Schedule self-deletion
        FileSynchronizer.ScheduleSelfCleanup();

        Console.WriteLine();
        Console.WriteLine("============================================");
        Console.WriteLine("  更新完成！");
        Console.WriteLine("============================================");
        return 0;
    }

    private int HandleNoOldVersion()
    {
        Console.WriteLine("没有找到旧版本程序位置。");
        Console.WriteLine();

        var message = "没有找到旧版本程序位置。\n\n" +
                      $"是否将路径写入配置文件所在位置的文件夹\n" +
                      $"并直接启动新版本程序？\n\n" +
                      $"当前路径: {_newDirectory}";

        var result = MessageBox.Show(
            message,
            "自动更新",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.No)
            return 1;

        // Write current directory as config dir
        RegistryHelper.WriteConfigDir(_newDirectory);

        // Start the new version
        var newExePath = Path.Combine(_newDirectory, "taskmonitor114.exe");
        if (File.Exists(newExePath))
        {
            ProcessManager.StartNewVersion(newExePath);
            Console.WriteLine("新版本程序已启动。");
            Console.WriteLine();
            Console.WriteLine("您可以安全地手动删除旧版本程序文件。");
        }
        else
        {
            Console.Error.WriteLine($"错误：当前目录未找到 taskmonitor114.exe");
            Console.Error.WriteLine($"路径: {newExePath}");
            MessageBox.Show(
                $"错误：在当前目录未找到 taskmonitor114.exe。\n\n路径: {newExePath}",
                "自动更新",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        FileSynchronizer.ScheduleSelfCleanup();
        return 0;
    }
}
