using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;

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

            // No cached mapping, try to get from running process
            string? exePath = null;
            try
            {
                var runningProc = Process.GetProcessesByName(
                    processName.EndsWith(".exe")
                        ? processName[..^4]
                        : processName
                ).FirstOrDefault();

                exePath = runningProc?.MainModule?.FileName;
            }
            catch
            {
                // Process not running or access denied
            }

            // If process is not running and no cache, return default
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

            // Extract icon and calculate hash
            byte[] pngData;
            string c1, c2, c3;
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon == null)
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

                using var bitmap = icon.ToBitmap();
                (c1, c2, c3) = ExtractColors(bitmap);

                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                pngData = ms.ToArray();
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    processName,
                    iconHash = "default",
                    iconData = "",
                    colorPrimary = "#6B7FD7",
                    colorSecondary = "#DD7596",
                    colorAccent = "#06D6A0",
                    error = ex.Message
                });
            }

            var hash = Convert.ToHexString(SHA256.HashData(pngData)).ToLowerInvariant();

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
                IconData = pngData,
                ColorPrimary = c1,
                ColorSecondary = c2,
                ColorAccent = c3
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
                iconData = Convert.ToBase64String(pngData),
                colorPrimary = c1,
                colorSecondary = c2,
                colorAccent = c3
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
