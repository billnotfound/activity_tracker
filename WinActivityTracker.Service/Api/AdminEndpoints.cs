using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Service.Native;

namespace WinActivityTracker.Service.Api;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", GetStatus);
        app.MapGet("/api/settings", GetSettings);
        app.MapPut("/api/settings", PutSettings);
        app.MapGet("/api/paths", GetPaths);
        app.MapPut("/api/paths", PutPaths);
        app.MapGet("/api/db/stats", GetDbStats);
        app.MapPost("/api/db/cleanup", RunCleanup);
        app.MapPost("/api/db/reset", RunReset);
        app.MapPost("/api/db/vacuum", RunVacuum);
    }

    private static IResult GetStatus(SettingsService settings) =>
        Results.Ok(new
        {
            status = "running",
            trackingEnabled = settings.Settings.TrackingEnabled,
            timestamp = DateTime.UtcNow
        });

    private static IResult GetSettings(SettingsService settings) =>
        Results.Ok(settings.Settings);

    private static IResult PutSettings(TrackerSettings input, SettingsService settings)
    {
        var oldAutoStart = settings.Settings.AutoStartEnabled;
        settings.Update(input);
        if (input.AutoStartEnabled != oldAutoStart)
            TrayApplicationContext.WriteRegistryAutoStart(input.AutoStartEnabled);
        return Results.Ok(settings.Settings);
    }

    private static IResult GetPaths(AppPaths appPaths)
    {
        var (regCfg, regData) = AppPaths.ReadRegistryValues();
        return Results.Ok(new
        {
            configDir = appPaths.ConfigDir,
            dataDir = appPaths.DataDir,
            registry = new { configDir = regCfg, dataDir = regData },
            message = I18nService._("admin.pathsChangeMessage")
        });
    }

    private static IResult PutPaths(PathInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ConfigDir))
        {
            try { Directory.CreateDirectory(input.ConfigDir); }
            catch (Exception ex) { return Results.BadRequest(new { error = I18nService._("admin.cannotCreateConfigDir", ex.Message) }); }
        }
        if (!string.IsNullOrWhiteSpace(input.DataDir))
        {
            try { Directory.CreateDirectory(input.DataDir); }
            catch (Exception ex) { return Results.BadRequest(new { error = I18nService._("admin.cannotCreateDataDir", ex.Message) }); }
        }

        AppPaths.WriteRegistry(
            string.IsNullOrWhiteSpace(input.ConfigDir) ? null : input.ConfigDir.Trim(),
            string.IsNullOrWhiteSpace(input.DataDir) ? null : input.DataDir.Trim());

        return Results.Ok(new
        {
            message = I18nService._("admin.pathsSaved"),
            configDir = input.ConfigDir,
            dataDir = input.DataDir
        });
    }

    private static async Task<IResult> GetDbStats(AppDbContext db)
    {
        var now = DateTime.UtcNow;

        var stats = await db.Database.SqlQuery<DbStatsRow>($"""
            SELECT
                (SELECT COUNT(*) FROM FocusChanges) AS FocusCount,
                (SELECT COUNT(*) FROM WindowSnapshots) AS WindowCount,
                (SELECT COUNT(*) FROM WindowSessions) AS SessionCount,
                (SELECT COUNT(*) FROM ProcessSnapshots) AS ProcessCount,
                (SELECT COUNT(*) FROM ProcessSessions) AS ProcessSessionCount,
                (SELECT COUNT(*) FROM MediaSessionRecords) AS MediaCount,
                (SELECT COUNT(*) FROM SystemEvents) AS SystemEventCount,
                (SELECT MIN(Timestamp) FROM FocusChanges) AS OldestRecord
        """).FirstAsync();

        return Results.Ok(new
        {
            focusChanges = stats.FocusCount,
            windowSnapshots = stats.WindowCount,
            windowSessions = stats.SessionCount,
            processSnapshots = stats.ProcessCount,
            processSessions = stats.ProcessSessionCount,
            mediaRecords = stats.MediaCount,
            systemEvents = stats.SystemEventCount,
            oldestRecord = stats.OldestRecord,
            newRecordsPerDay = Math.Round(
                stats.FocusCount / Math.Max(1, (now - (stats.OldestRecord ?? now)).TotalDays), 1)
        });
    }

    private static async Task<IResult> RunCleanup(
        int? days, bool? vacuum, AppDbContext db, SettingsService settings)
    {
        var retention = days ?? settings.Settings.DataRetentionDays;
        var cutoff = DateTime.UtcNow.AddDays(-retention);

        var deletedFocus = await db.FocusChanges.Where(f => f.Timestamp < cutoff).ExecuteDeleteAsync();
        var deletedWindows = await db.WindowSnapshots.Where(w => w.Timestamp < cutoff).ExecuteDeleteAsync();
        var deletedSessions = await db.WindowSessions.Where(w => w.OpenTime < cutoff).ExecuteDeleteAsync();
        var deletedProcesses = await db.ProcessSnapshots.Where(p => p.Timestamp < cutoff).ExecuteDeleteAsync();
        var deletedProcSessions = await db.ProcessSessions.Where(p => p.StartTime < cutoff).ExecuteDeleteAsync();
        var deletedMedia = await db.MediaSessionRecords.Where(m => m.StartTime < cutoff).ExecuteDeleteAsync();
        var deletedSystemEvents = await db.SystemEvents.Where(e => e.Timestamp < cutoff).ExecuteDeleteAsync();

        await db.Database.ExecuteSqlRawAsync("PRAGMA optimize");

        if (vacuum == true)
            await db.Database.ExecuteSqlRawAsync("VACUUM");

        return Results.Ok(new
        {
            retentionDays = retention,
            cutoff = cutoff,
            vacuumed = vacuum == true,
            deleted = new
            {
                focusChanges = deletedFocus,
                windowSnapshots = deletedWindows,
                windowSessions = deletedSessions,
                processSnapshots = deletedProcesses,
                processSessions = deletedProcSessions,
                mediaRecords = deletedMedia,
                systemEvents = deletedSystemEvents
            }
        });
    }

    private static async Task<IResult> RunReset(bool? confirm, AppDbContext db)
    {
        if (confirm != true)
            return Results.BadRequest(new { error = "Set ?confirm=true to delete all data. This cannot be undone." });

        var deletedFocus = await db.FocusChanges.ExecuteDeleteAsync();
        var deletedWindows = await db.WindowSnapshots.ExecuteDeleteAsync();
        var deletedSessions = await db.WindowSessions.ExecuteDeleteAsync();
        var deletedProcesses = await db.ProcessSnapshots.ExecuteDeleteAsync();
        var deletedProcSessions = await db.ProcessSessions.ExecuteDeleteAsync();
        var deletedMedia = await db.MediaSessionRecords.ExecuteDeleteAsync();
        var deletedSystemEvents = await db.SystemEvents.ExecuteDeleteAsync();

        await db.Database.ExecuteSqlRawAsync("VACUUM");

        return Results.Ok(new
        {
            message = "All data deleted. Database schema preserved.",
            deleted = new
            {
                focusChanges = deletedFocus,
                windowSnapshots = deletedWindows,
                windowSessions = deletedSessions,
                processSnapshots = deletedProcesses,
                processSessions = deletedProcSessions,
                mediaRecords = deletedMedia,
                systemEvents = deletedSystemEvents
            }
        });
    }

    private static async Task<IResult> RunVacuum(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("VACUUM");
        return Results.Ok(new { message = "VACUUM complete." });
    }
}

internal sealed record PathInput(string? ConfigDir, string? DataDir);

internal sealed class DbStatsRow
{
    public int FocusCount { get; set; }
    public int WindowCount { get; set; }
    public int SessionCount { get; set; }
    public int ProcessCount { get; set; }
    public int ProcessSessionCount { get; set; }
    public int MediaCount { get; set; }
    public int SystemEventCount { get; set; }
    public DateTime? OldestRecord { get; set; }
}
