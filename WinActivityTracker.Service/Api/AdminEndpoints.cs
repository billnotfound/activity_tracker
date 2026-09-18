using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
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

    private static async Task<IResult> PutSettings(HttpContext context, SettingsService settings)
    {
        using var doc = await JsonDocument.ParseAsync(context.Request.Body);
        var json = doc.RootElement;

        var oldAutoStart = settings.Settings.AutoStartEnabled;
        settings.Update(json);

        if (json.TryGetProperty("autoStartEnabled", out var autoProp)
            || json.TryGetProperty("AutoStartEnabled", out autoProp))
        {
            var newAutoStart = autoProp.GetBoolean();
            if (newAutoStart != oldAutoStart)
                TrayApplicationContext.WriteRegistryAutoStart(newAutoStart);
        }

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
        if (retention < 1)
            return Results.BadRequest(new { error = "Retention days must be at least 1." });

        DateTime cutoff;
        try { cutoff = DateTime.UtcNow.AddDays(-retention); }
        catch (ArgumentOutOfRangeException)
        {
            return Results.BadRequest(new { error = "Retention days are outside the supported range." });
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        int deletedFocus, deletedWindows, deletedSessions, deletedProcesses,
            deletedProcSessions, deletedMedia, deletedSystemEvents;
        try
        {
            (deletedFocus, deletedSystemEvents) =
                await DeleteExpiredIntervalsAsync(db, cutoff);
            deletedWindows = await db.WindowSnapshots.Where(w => w.Timestamp < cutoff).ExecuteDeleteAsync();
            (deletedSessions, deletedProcSessions, deletedMedia) =
                await DeleteExpiredActivityAsync(db, cutoff);
            deletedProcesses = await db.ProcessSnapshots.Where(p => p.Timestamp < cutoff).ExecuteDeleteAsync();
            await db.TimeOffsetApplications
                .Where(j => j.MaxRowTimestamp < cutoff
                    && (j.RevertedAt != null
                        || db.TimeAnomalies.Any(a => a.Id == j.AnomalyId && a.DetectedAt < cutoff)
                        || !db.TimeAnomalies.Any(a => a.Id == j.AnomalyId)))
                .ExecuteDeleteAsync();
            await db.TimeAnomalies
                .Where(a => a.DetectedAt < cutoff
                    && !db.TimeOffsetApplications.Any(j => j.AnomalyId == a.Id))
                .ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        await db.Database.ExecuteSqlRawAsync("PRAGMA optimize");

        JournalCompactionResult? journalCompaction = null;
        if (vacuum == true)
            journalCompaction = await ReclaimStorageAsync(db);

        return Results.Ok(new
        {
            retentionDays = retention,
            cutoff = cutoff,
            vacuumed = vacuum == true,
            compactedRepairJournals = journalCompaction?.CompactedRows ?? 0,
            repairJournalBytesSaved = journalCompaction?.BytesSaved ?? 0,
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

    public static async Task<(int FocusChanges, int SystemEvents)> DeleteExpiredIntervalsAsync(
        AppDbContext db, DateTime cutoff)
    {
        var focusChanges = await db.FocusChanges
            .Where(f => f.Timestamp < cutoff
                && (f.DurationSeconds <= 0 || f.Timestamp.AddSeconds(f.DurationSeconds) < cutoff))
            .ExecuteDeleteAsync();
        var systemEvents = await db.SystemEvents
            .Where(e => e.Timestamp < cutoff
                && (e.DurationSeconds <= 0 || e.Timestamp.AddSeconds(e.DurationSeconds) < cutoff))
            .ExecuteDeleteAsync();
        return (focusChanges, systemEvents);
    }

    public static async Task<(int Sessions, int ProcSessions, int Media)> DeleteExpiredActivityAsync(
        AppDbContext db, DateTime cutoff)
    {
        var sessions = await db.WindowSessions
            .Where(w => w.OpenTime < cutoff && w.CloseTime != null && w.CloseTime < cutoff)
            .ExecuteDeleteAsync();
        var procSessions = await db.ProcessSessions
            .Where(p => p.StartTime < cutoff && p.EndTime != null && p.EndTime < cutoff)
            .ExecuteDeleteAsync();
        var media = await db.MediaSessionRecords
            .Where(m => m.StartTime < cutoff && m.EndTime != null && m.EndTime < cutoff)
            .ExecuteDeleteAsync();
        return (sessions, procSessions, media);
    }

    private static async Task<IResult> RunReset(bool? confirm, AppDbContext db)
    {
        if (confirm != true)
            return Results.BadRequest(new { error = "Set ?confirm=true to delete all data. This cannot be undone." });

        var deleted = await DeleteAllUserRowsAsync(db);

        await db.Database.ExecuteSqlRawAsync("VACUUM");

        return Results.Ok(new
        {
            message = "All data deleted. Database schema preserved.",
            deleted
        });
    }

    /// <summary>
    /// 全量重置核心:事务内删除全部用户表数据。不清 sqlite_sequence——保留自增计数,
    /// 防止重置后新行复用旧 Id(tracker 内存缓存的陈旧 DbId 会误更新到新行)。
    /// </summary>
    public static async Task<Dictionary<string, int>> DeleteAllUserRowsAsync(AppDbContext db)
    {
        var tables = await db.Database.SqlQuery<string>($"""
            SELECT name AS Value FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
        """).ToListAsync();

        var deleted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var table in tables)
        {
            // Names come from sqlite_master and are quoted as SQLite identifiers.
            var quoted = QuoteSqliteIdentifier(table);
#pragma warning disable EF1003
            deleted[table] = await db.Database.ExecuteSqlRawAsync("DELETE FROM " + quoted);
#pragma warning restore EF1003
        }
        await tx.CommitAsync();
        return deleted;
    }

    private static async Task<IResult> RunVacuum(AppDbContext db)
    {
        var compacted = await ReclaimStorageAsync(db);
        return Results.Ok(new
        {
            message = "Database optimization complete.",
            compactedRepairJournals = compacted.CompactedRows,
            repairJournalBytesSaved = compacted.BytesSaved
        });
    }

    private static async Task<JournalCompactionResult> ReclaimStorageAsync(AppDbContext db)
    {
        var compacted = await TimeOffsetJournalCodec.CompactLegacyAsync(db);
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        await SqliteMaintenance.TryTruncateWalAsync(db);
        await db.Database.ExecuteSqlRawAsync("VACUUM");
        await SqliteMaintenance.TryTruncateWalAsync(db);
        return compacted;
    }

    private static string QuoteSqliteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";
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
