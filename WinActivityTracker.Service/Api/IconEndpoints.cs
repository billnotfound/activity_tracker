using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Api;

public static class IconEndpoints
{
    public static void MapIconEndpoints(this WebApplication app)
    {
        app.MapGet("/api/icons/{processName}", GetProcessIcon);
        app.MapDelete("/api/icons/cache", ClearIconCache);
    }

    private static async Task<IResult> GetProcessIcon(
        string processName,
        AppDbContext db)
    {
        try
        {
            // Normalize process name
            processName = processName.Trim();
            if (string.IsNullOrEmpty(processName))
                return Results.BadRequest(new { error = "Process name is required" });

            // First, try to find cached mapping for this process name
            var mapping = await db.ProcessIconMappings
                .Where(m => m.ProcessName == processName)
                .OrderByDescending(m => m.LastSeen)
                .FirstOrDefaultAsync();

            if (mapping != null)
            {
                // Found cached mapping, get the icon from database
                var cachedIcon = await db.ProcessIcons
                    .FirstOrDefaultAsync(i => i.IconHash == mapping.IconHash);

                if (cachedIcon != null)
                {
                    return Results.Ok(new
                    {
                        processName,
                        iconHash = cachedIcon.IconHash,
                        iconData = Convert.ToBase64String(cachedIcon.IconData),
                        colorPrimary = cachedIcon.ColorPrimary,
                        colorSecondary = cachedIcon.ColorSecondary,
                        colorAccent = cachedIcon.ColorAccent,
                        cached = true
                    });
                }
            }

            // No cached icon data — try live PID→path resolution.
            // Uses QueryFullProcessImageName which works cross-integrity-level,
            // unlike Process.MainModule.FileName which fails for admin processes.
            string? exePath = null;
            try
            {
                var runningProc = Process.GetProcessesByName(
                    processName.EndsWith(".exe")
                        ? processName[..^4]
                        : processName
                ).FirstOrDefault();

                if (runningProc != null)
                    exePath = IconService.GetProcessImagePath(runningProc.Id);
            }
            catch
            {
                // Process not running or unexpected error
            }

            // Fallback: use ExePath from a previous successful extraction.
            // The .exe file on disk is usually readable by all users — this
            // recovers icons for admin processes that were cached when non-elevated.
            if (string.IsNullOrEmpty(exePath) && mapping != null && !string.IsNullOrEmpty(mapping.ExePath))
                exePath = mapping.ExePath;

            if (string.IsNullOrEmpty(exePath))
            {
                return Results.Ok(new
                {
                    processName,
                    iconHash = "default",
                    iconData = "",
                    colorPrimary = "#6B7FD7",
                    colorSecondary = "#DD7596",
                    colorAccent = "#06D6A0",
                    note = "Process not currently running and no cached icon available."
                });
            }

            // Extract icon and calculate hash (CPU-bound — offload from request thread)
            var iconResult = await Task.Run(() =>
            {
                try
                {
                    using var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon == null) return null;

                    using var bitmap = icon.ToBitmap();
                    var colors = ExtractColors(bitmap);

                    using var ms = new MemoryStream();
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return new IconExtractResult
                    {
                        PngData = ms.ToArray(),
                        ColorPrimary = colors.primary,
                        ColorSecondary = colors.secondary,
                        ColorAccent = colors.accent
                    };
                }
                catch (Exception ex)
                {
                    return new IconExtractResult { Error = ex.Message };
                }
            });

            if (iconResult == null)
            {
                return Results.Ok(new
                {
                    processName,
                    iconHash = "default",
                    iconData = "",
                    colorPrimary = "#6B7FD7",
                    colorSecondary = "#DD7596",
                    colorAccent = "#06D6A0"
                });
            }

            if (iconResult.Error != null)
            {
                return Results.Ok(new
                {
                    processName,
                    iconHash = "default",
                    iconData = "",
                    colorPrimary = "#6B7FD7",
                    colorSecondary = "#DD7596",
                    colorAccent = "#06D6A0",
                    error = iconResult.Error
                });
            }

            var hash = Convert.ToHexString(SHA256.HashData(iconResult.PngData)).ToLowerInvariant();

            // Check if icon already exists in database
            var existingIcon = await db.ProcessIcons.FirstOrDefaultAsync(i => i.IconHash == hash);
            if (existingIcon != null)
            {
                // Update or create mapping for this process name
                var existingMapping = await db.ProcessIconMappings
                    .FirstOrDefaultAsync(m => m.ProcessName == processName && m.ExePath == exePath);

                if (existingMapping != null)
                {
                    existingMapping.LastSeen = DateTime.UtcNow;
                }
                else
                {
                    db.ProcessIconMappings.Add(new ProcessIconMapping
                    {
                        ProcessName = processName,
                        ExePath = exePath,
                        IconHash = hash,
                        LastSeen = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();

                // Return existing icon data from database
                return Results.Ok(new
                {
                    processName,
                    iconHash = existingIcon.IconHash,
                    iconData = Convert.ToBase64String(existingIcon.IconData),
                    colorPrimary = existingIcon.ColorPrimary,
                    colorSecondary = existingIcon.ColorSecondary,
                    colorAccent = existingIcon.ColorAccent
                });
            }

            // Store new icon in database
            db.ProcessIcons.Add(new ProcessIcon
            {
                IconHash = hash,
                IconData = iconResult.PngData,
                ColorPrimary = iconResult.ColorPrimary,
                ColorSecondary = iconResult.ColorSecondary,
                ColorAccent = iconResult.ColorAccent
            });

            // Create mapping for this process name
            db.ProcessIconMappings.Add(new ProcessIconMapping
            {
                ProcessName = processName,
                ExePath = exePath,
                IconHash = hash,
                LastSeen = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                processName,
                iconHash = hash,
                iconData = Convert.ToBase64String(iconResult.PngData),
                colorPrimary = iconResult.ColorPrimary,
                colorSecondary = iconResult.ColorSecondary,
                colorAccent = iconResult.ColorAccent
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get icon: {ex.Message}");
        }
    }

    private static async Task<IResult> ClearIconCache(AppDbContext db)
    {
        try
        {
            var count = await db.ProcessIcons.CountAsync();
            db.ProcessIcons.RemoveRange(db.ProcessIcons);
            await db.SaveChangesAsync();
            return Results.Ok(new { message = $"Cleared {count} icons from cache" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to clear icon cache: {ex.Message}");
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
}

internal sealed class IconExtractResult
{
    public byte[] PngData { get; set; } = [];
    public string ColorPrimary { get; set; } = "#6B7FD7";
    public string ColorSecondary { get; set; } = "#DD7596";
    public string ColorAccent { get; set; } = "#06D6A0";
    public string? Error { get; set; }
}
