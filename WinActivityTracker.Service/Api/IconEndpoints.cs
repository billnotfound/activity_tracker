using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Service.Api;

public static class IconEndpoints
{
    public static void MapIconEndpoints(this WebApplication app)
    {
        app.MapGet("/api/icons/{processName}", GetProcessIcon);
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

            // Find a recent process session with this name to get its path
            var recentSession = await db.ProcessSessions
                .Where(p => p.ProcessName == processName)
                .OrderByDescending(p => p.StartTime)
                .FirstOrDefaultAsync();

            if (recentSession == null)
            {
                return Results.NotFound(new { error = "No process session found for this name" });
            }

            // Try to get the executable path
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

            // If we can't get the path, return a default icon
            if (string.IsNullOrEmpty(exePath))
            {
                return Results.Ok(new
                {
                    processName,
                    iconHash = "default",
                    iconData = "",
                    colorPrimary = "#FFD700",
                    colorSecondary = "#FF1493",
                    colorAccent = "#00CED1"
                });
            }

            // Query icon from database by trying to find matching hash
            // For now, return default colors - IconService will populate icons in background
            var icon = await db.ProcessIcons
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync();

            if (icon == null)
            {
                return Results.Ok(new
                {
                    processName,
                    iconHash = "default",
                    iconData = "",
                    colorPrimary = "#FFD700",
                    colorSecondary = "#FF1493",
                    colorAccent = "#00CED1"
                });
            }

            return Results.Ok(new
            {
                processName,
                iconHash = icon.IconHash,
                iconData = Convert.ToBase64String(icon.IconData),
                colorPrimary = icon.ColorPrimary,
                colorSecondary = icon.ColorSecondary,
                colorAccent = icon.ColorAccent
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get icon: {ex.Message}");
        }
    }
}
