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
    private readonly SystemPressure _systemPressure;
    private readonly ILogger<IconService> _logger;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase); // path → hash
    private readonly HashSet<string> _mappedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _extractionGate = new(1, 1);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Func<int, string?> _getImagePath;
    private readonly Func<string, IconImage?> _extractImage;
    private readonly Func<string, IReadOnlyList<int>> _findProcessIds;

    public IconService(IServiceScopeFactory scopeFactory, SystemPressure systemPressure, ILogger<IconService> logger)
        : this(scopeFactory, systemPressure, logger, GetProcessImagePath, ExtractImage, FindProcessIds)
    {
    }

    internal IconService(IServiceScopeFactory scopeFactory, SystemPressure systemPressure, ILogger<IconService> logger,
        Func<int, string?> getImagePath, Func<string, IconImage?> extractImage,
        Func<string, IReadOnlyList<int>> findProcessIds)
    {
        _scopeFactory = scopeFactory;
        _systemPressure = systemPressure;
        _logger = logger;
        _getImagePath = getImagePath;
        _extractImage = extractImage;
        _findProcessIds = findProcessIds;
    }

    /// <summary>
    /// Fire-and-forget: ensures icons exist for the given list of PIDs.
    /// Already-cached paths are skipped immediately.
    /// </summary>
    public async Task EnsureIconsAsync(List<int> pids)
    {
        // A slow shell/DB operation must not accumulate a new batch every poll.
        if (!await _scanGate.WaitAsync(0)) return;
        try
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
        finally { _scanGate.Release(); }
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

    internal async Task<bool> EnsureIconAsync(int pid, string? processName = null, bool forceRefresh = false)
    {
        if (_systemPressure.GetCurrent() != PressureLevel.Normal)
        {
            _logger.LogDebug("Deferring icon extraction for PID {Pid} (system pressure)", pid);
            return false;
        }

        string? path;
        try
        {
            path = _getImagePath(pid);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(path)) return false;
        processName = string.IsNullOrEmpty(processName) ? Path.GetFileName(path) : processName;
        var mappingKey = path + '\0' + processName;

        await _extractionGate.WaitAsync();
        try
        {
            if (!forceRefresh && _mappedPaths.Contains(mappingKey)) return true;

            IconImage? image = null;
            if (forceRefresh || !_cache.TryGetValue(path, out var hash))
            {
                image = _extractImage(path);
                if (image == null) return false;
                hash = Convert.ToHexString(SHA256.HashData(image.PngData)).ToLowerInvariant();
            }
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existingIcon = await db.ProcessIcons.FirstOrDefaultAsync(i => i.IconHash == hash);
            if (existingIcon == null && image == null)
            {
                image = _extractImage(path);
                if (image == null) return false;
                hash = Convert.ToHexString(SHA256.HashData(image.PngData)).ToLowerInvariant();
                existingIcon = await db.ProcessIcons.FirstOrDefaultAsync(i => i.IconHash == hash);
            }
            if (existingIcon == null)
            {
                db.ProcessIcons.Add(new ProcessIcon
                {
                    IconHash = hash,
                    IconData = image!.PngData,
                    ColorPrimary = image.Primary,
                    ColorSecondary = image.Secondary,
                    ColorAccent = image.Accent
                });
            }

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
                }
                else
                {
                    latestMapping.LastSeen = DateTime.UtcNow;
                }
            }
            // Publish the cache entry only after both the icon and mapping commit.
            // A failed write must remain retryable, not leave a permanent pending hit.
            await db.SaveChangesAsync();
            _cache[path] = hash;
            _mappedPaths.Add(mappingKey);
            return true;
        }
        finally { _extractionGate.Release(); }
    }

    internal sealed record IconImage(byte[] PngData, string Primary, string Secondary, string Accent);

    private static IconImage? ExtractImage(string path)
    {
        using var icon = Icon.ExtractAssociatedIcon(path);
        if (icon == null) return null;
        using var bitmap = icon.ToBitmap();
        var (primary, secondary, accent) = ExtractColors(bitmap);
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return new IconImage(ms.ToArray(), primary, secondary, accent);
    }

    public static IReadOnlyList<int> FindProcessIds(string processName)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
        try { return processes.Select(process => process.Id).ToArray(); }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public async Task InvalidateCacheAsync()
    {
        await _extractionGate.WaitAsync();
        try
        {
            _cache.Clear();
            _mappedPaths.Clear();
        }
        finally { _extractionGate.Release(); }
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

        return await (from mapping in db.ProcessIconMappings
            join icon in db.ProcessIcons on mapping.IconHash equals icon.IconHash
            select mapping.ProcessName)
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
    public async Task<bool> ExtractAndSaveIconAsync(string processName, bool forceRefresh = false)
    {
        try
        {
            var pids = _findProcessIds(processName);
            if (pids.Count == 0)
            {
                _logger.LogDebug("No running process found for {ProcessName}", processName);
                return false;
            }

            foreach (var pid in pids)
                if (await EnsureIconAsync(pid, processName, forceRefresh)) return true;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract icon for {ProcessName}", processName);
            return false;
        }
    }
}
