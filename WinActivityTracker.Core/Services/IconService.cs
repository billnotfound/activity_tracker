using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Interop;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// Collects icons for visible processes, stores in DB deduplicated by content hash,
/// and extracts 3 dominant colors (Material Design primary/secondary/accent).
/// Uses in-memory cache to avoid re-extraction within the same session.
/// </summary>
public class IconService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IconService> _logger;
    private readonly ConcurrentDictionary<string, string> _cache = new(); // path → hash

    public IconService(IServiceScopeFactory scopeFactory, ILogger<IconService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Fire-and-forget: ensures icons exist for the given list of PIDs.
    /// Already-cached paths are skipped immediately.
    /// </summary>
    public async Task EnsureIconsAsync(List<int> pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                await EnsureIconAsync(pid);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IconService: icon extraction failed for PID {Pid}", pid);
            }
        }
    }

    /// <summary>
    /// Resolves PID → full executable path using QueryFullProcessImageName with
    /// PROCESS_QUERY_LIMITED_INFORMATION. Crosses integrity levels — works for
    /// admin-elevated processes where Process.MainModule.FileName would fail.
    /// </summary>
    public static string? GetProcessImagePath(int pid)
    {
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (hProcess == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(260);
            var size = (uint)sb.Capacity;
            return NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size)
                ? sb.ToString()
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private async Task EnsureIconAsync(int pid, string? processName = null)
    {
        string? path;
        try
        {
            path = GetProcessImagePath(pid);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(path)) return;

        // Dedup guard: if another caller is already processing this path, skip.
        if (_cache.ContainsKey(path)) return;
        if (!_cache.TryAdd(path, "__pending__")) return;

        byte[] pngData;
        string c1, c2, c3;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon == null) { _cache.TryRemove(path, out _); return; }
            using var bitmap = icon.ToBitmap();

            (c1, c2, c3) = ExtractColors(bitmap);

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngData = ms.ToArray();
        }
        catch (Exception ex)
        {
            _cache.TryRemove(path, out _);
            _logger.LogDebug(ex, "IconService: failed to extract icon from {Path}", path);
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(pngData)).ToLowerInvariant();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existingIcon = await db.ProcessIcons.FirstOrDefaultAsync(i => i.IconHash == hash);
        if (existingIcon == null)
        {
            db.ProcessIcons.Add(new ProcessIcon
            {
                IconHash = hash,
                IconData = pngData,
                ColorPrimary = c1,
                ColorSecondary = c2,
                ColorAccent = c3
            });
            await db.SaveChangesAsync();
            _logger.LogInformation("IconService: stored icon {Hash} ({Path}) colors={C1} {C2} {C3}",
                hash[..12], Path.GetFileName(path), c1, c2, c3);
        }

        _cache[path] = hash;

        // Version-aware mapping: if processName is known, track icon versions
        if (!string.IsNullOrEmpty(processName))
        {
            var latestMapping = await db.ProcessIconMappings
                .Where(m => m.ProcessName == processName && m.ExePath == path)
                .OrderByDescending(m => m.FirstSeen)
                .FirstOrDefaultAsync();

            if (latestMapping == null)
            {
                db.ProcessIconMappings.Add(new ProcessIconMapping
                {
                    ProcessName = processName,
                    ExePath = path,
                    IconHash = hash,
                    LastSeen = DateTime.UtcNow,
                    FirstSeen = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            else if (latestMapping.IconHash != hash)
            {
                _logger.LogInformation("IconService: icon changed for {Process} — old={OldHash} new={NewHash}",
                    processName, latestMapping.IconHash[..12], hash[..12]);
                db.ProcessIconMappings.Add(new ProcessIconMapping
                {
                    ProcessName = processName,
                    ExePath = path,
                    IconHash = hash,
                    LastSeen = DateTime.UtcNow,
                    FirstSeen = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            else
            {
                latestMapping.LastSeen = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Extracts the top 3 dominant colors from a bitmap, quantized for grouping.
    /// Skips near-transparent pixels.
    /// </summary>
    private static (string primary, string secondary, string accent) ExtractColors(Bitmap source)
    {
        using var small = new Bitmap(source, 16, 16);
        var counts = new Dictionary<int, int>();

        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            var c = small.GetPixel(x, y);
            if (c.A < 128) continue;

            // Quantize to 16 levels per channel (0,16,32,...,240) for grouping similar shades.
            int r = c.R / 16 * 16;
            int g = c.G / 16 * 16;
            int b = c.B / 16 * 16;
            var key = (r << 16) | (g << 8) | b;
            counts.TryGetValue(key, out var n);
            counts[key] = n + 1;
        }

        var top = counts.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList();

        string hex(int k) =>
            $"#{(k >> 16) & 0xFF:X2}{(k >> 8) & 0xFF:X2}{k & 0xFF:X2}";

        return (
            top.Count > 0 ? hex(top[0]) : "#000000",
            top.Count > 1 ? hex(top[1]) : "#000000",
            top.Count > 2 ? hex(top[2]) : "#000000"
        );
    }

    /// <summary>
    /// Gets all process names that have icons extracted.
    /// Used by IconCacheService to populate the cache on startup.
    /// </summary>
    public async Task<List<string>> GetAllProcessNamesWithIconsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ProcessIconMappings
            .Select(m => m.ProcessName)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// Returns the icon (PNG data + colors) valid at the given point in time.
    /// Finds the mapping with the largest FirstSeen ≤ at, then joins to ProcessIcons.
    /// Returns null if no icon was known at that time.
    /// </summary>
    public async Task<ProcessIcon?> GetIconForTimeAsync(string processName, DateTime at)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mapping = await db.ProcessIconMappings
            .Where(m => m.ProcessName == processName && m.FirstSeen <= at)
            .OrderByDescending(m => m.FirstSeen)
            .FirstOrDefaultAsync();

        if (mapping == null) return null;

        return await db.ProcessIcons
            .FirstOrDefaultAsync(i => i.IconHash == mapping.IconHash);
    }

    /// <summary>
    /// Extracts and saves icon for a specific process name.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public async Task<bool> ExtractAndSaveIconAsync(string processName)
    {
        try
        {
            // Find a running process with this name
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
            if (processes.Length == 0)
            {
                _logger.LogDebug("No running process found for {ProcessName}", processName);
                return false;
            }

            var pid = processes[0].Id;
            await EnsureIconAsync(pid, processName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract icon for {ProcessName}", processName);
            return false;
        }
    }
}
