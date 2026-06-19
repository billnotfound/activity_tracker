using System.Runtime.InteropServices;

namespace WinActivityTracker.Updater;

/// <summary>
/// Compares manifests, deletes removed files, copies new files.
/// NEVER deletes files not in the manifest (user data protection).
/// NEVER deletes directories.
/// </summary>
internal static class FileSynchronizer
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 500;

    // Double protection: never delete these regardless of manifest
    private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings.json", "tags.json", "title_rules.json"
    };

    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db", ".sqlite", ".sqlite3", ".bak"
    };

    /// <summary>
    /// Delete files that existed in the old version but are no longer in the new version.
    /// </summary>
    public static void DeleteRemovedFiles(
        string oldDirectory,
        ResourceManifest oldManifest,
        ResourceManifest newManifest)
    {
        var removedFiles = newManifest.GetRemovedFiles(oldManifest);
        foreach (var relativePath in removedFiles)
        {
            if (IsProtectedFile(relativePath))
            {
                Console.WriteLine($"  跳过受保护文件: {relativePath}");
                continue;
            }

            var fullPath = Path.Combine(oldDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            Console.WriteLine($"  删除: {relativePath}");
            if (!TryDeleteWithRetry(fullPath))
            {
                ScheduleFileDelete(fullPath);
                Console.WriteLine($"  已安排重启后删除: {relativePath}");
            }
        }
    }

    /// <summary>
    /// Copy all files from the new manifest to the old directory.
    /// Overwrites existing files with newer versions.
    /// </summary>
    public static void CopyNewFiles(
        string newDirectory,
        string oldDirectory,
        ResourceManifest newManifest)
    {
        foreach (var relativePath in newManifest.FileEntries)
        {
            var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var sourcePath = Path.Combine(newDirectory, normalizedPath);
            var destPath = Path.Combine(oldDirectory, normalizedPath);

            if (!File.Exists(sourcePath))
            {
                Console.Error.WriteLine($"  源文件缺失: {relativePath}");
                continue;
            }

            // Ensure destination directory exists
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null) Directory.CreateDirectory(destDir);

            // Skip if identical
            if (File.Exists(destPath) && FilesAreIdentical(sourcePath, destPath))
                continue;

            Console.WriteLine($"  复制: {relativePath}");
            if (!TryCopyWithRetry(sourcePath, destPath))
            {
                Console.Error.WriteLine($"  复制失败: {relativePath}");
            }
        }
    }

    /// <summary>
    /// Ensure all directory entries from the manifest exist in the target.
    /// </summary>
    public static void EnsureDirectories(string oldDirectory, ResourceManifest newManifest)
    {
        foreach (var dirEntry in newManifest.DirectoryEntries)
        {
            var dirPath = Path.Combine(oldDirectory,
                dirEntry.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dirPath);
        }
    }

    /// <summary>
    /// Schedule the updater itself for deletion on next reboot.
    /// Uses MoveFileEx with MOVEFILE_DELAY_UNTIL_REBOOT.
    /// </summary>
    public static void ScheduleSelfCleanup()
    {
        var updaterPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(updaterPath))
        {
            Console.WriteLine("无法确定更新程序路径，跳过自清理。");
            return;
        }

        try
        {
            if (Win32Native.MoveFileEx(updaterPath, null,
                Win32Native.MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT))
            {
                Console.WriteLine("更新程序将在重启后删除。");
            }
            else
            {
                Console.WriteLine($"自清理失败 (error: {Marshal.GetLastWin32Error()})。");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"自清理异常: {ex.Message}");
        }
    }

    private static bool IsProtectedFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (ProtectedFileNames.Contains(fileName)) return true;
        var ext = Path.GetExtension(relativePath);
        return ProtectedExtensions.Contains(ext);
    }

    private static bool TryDeleteWithRetry(string path)
    {
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception) when (i < MaxRetries - 1)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    }

    private static bool TryCopyWithRetry(string source, string dest)
    {
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                File.Copy(source, dest, overwrite: true);
                return true;
            }
            catch (Exception) when (i < MaxRetries - 1)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    }

    private static bool FilesAreIdentical(string path1, string path2)
    {
        try
        {
            var info1 = new FileInfo(path1);
            var info2 = new FileInfo(path2);
            if (info1.Length != info2.Length) return false;

            // Quick: timestamp match
            if (info1.LastWriteTimeUtc == info2.LastWriteTimeUtc)
            {
                // Byte compare only for reasonably small files
                if (info1.Length < 1024 * 1024)
                {
                    return File.ReadAllBytes(path1).AsSpan()
                        .SequenceEqual(File.ReadAllBytes(path2));
                }
                // Large file with same size + timestamp: assume identical
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void ScheduleFileDelete(string path)
    {
        Win32Native.MoveFileEx(path, null,
            Win32Native.MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT);
    }
}
