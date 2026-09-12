// Pre-flight checks for taskmonitor114 startup (extracted from Program.cs).
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace WinActivityTracker.Service;

internal static class ProgramStartup
{
    /// <summary>
    /// Single-instance guard via PID file in DataDir. Writes the current PID to
    /// taskmonitor114.pid on success; DeletePidFile() removes it on shutdown.
    /// Returns false if another instance is running.
    /// </summary>
    public static bool EnsureSingleInstance(string dataDir, bool silent)
    {
        var pidFile = Path.Combine(dataDir, "taskmonitor114.pid");
        var myPid = Environment.ProcessId;

        if (File.Exists(pidFile))
        {
            try
            {
                var text = File.ReadAllText(pidFile).Trim();
                if (int.TryParse(text, out var existingPid) && existingPid != myPid)
                {
                    try
                    {
                        using var p = System.Diagnostics.Process.GetProcessById(existingPid);
                        if (!silent)
                            MessageBox.Show(I18nService._("programStartup.alreadyRunningMessage"),
                                I18nService._("programStartup.alreadyRunningTitle"),
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Console.Error.WriteLine("Another instance is already running (PID: {0}).", existingPid);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        // Stale lock file: process no longer exists.
                        File.Delete(pidFile);
                    }
                }
            }
            catch
            {
                // Corrupted or inaccessible lock file: delete and continue.
                try { File.Delete(pidFile); } catch { }
            }
        }

        try
        {
            File.WriteAllText(pidFile, myPid.ToString());
        }
        catch
        {
            // Can't write PID file: not fatal, continue without guard.
        }

        return true;
    }

    /// <summary>
    /// Removes the PID file on clean shutdown.
    /// </summary>
    public static void DeletePidFile(string dataDir)
    {
        var pidFile = Path.Combine(dataDir, "taskmonitor114.pid");
        try
        {
            if (File.Exists(pidFile))
                File.Delete(pidFile);
        }
        catch { }
    }

    /// <summary>
    /// Reads ApiPort from settings.json (stripping // comment lines).
    /// Falls back to 32579 if the file doesn't exist or can't be parsed.
    /// </summary>
    public static int ParsePortFromSettings(string settingsPath, int defaultPort = 32579)
    {
        if (!File.Exists(settingsPath)) return defaultPort;

        try
        {
            var raw = File.ReadAllText(settingsPath);
            var clean = string.Join("\n", raw.Split('\n')
                .Select(l => l.TrimStart())
                .Where(l => !l.StartsWith("//")));
            using var doc = System.Text.Json.JsonDocument.Parse(clean);
            if (doc.RootElement.TryGetProperty("ApiPort", out var p) && p.TryGetInt32(out var v))
                return Math.Clamp(v, 1024, 65535);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.WriteLine($"Warning: failed to parse settings.json for port — {ex.Message}. Using default {defaultPort}.");
        }

        return defaultPort;
    }

    /// <summary>
    /// Tests whether the given port is available on loopback.
    /// Returns true if the port is free.
    /// </summary>
    public static bool IsPortAvailable(int port, bool silent)
    {
        try
        {
            using var test = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            test.Start();
            test.Stop();
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            if (!silent)
                MessageBox.Show(I18nService._("programStartup.portInUseMessage", port),
                    I18nService._("programStartup.portInUseTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Console.Error.WriteLine($"Port {port} is already in use.");
            return false;
        }
    }

    /// <summary>
    /// Ensures the database is created, WAL mode is enabled, and schema is migrated.
    /// </summary>
    public static async Task InitializeDatabase(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await EnsureMissingTables(db);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;PRAGMA cache_size=-2000");
        MigrateMediaSessions(db);
        MigrateProcessIconMappings(db);
        MigrateHeartbeatMonotonic(db);
        MigrateTimeAnomalyDirection(db);
        await EnsureMissingIndexes(db);

        // v1 repair journals stored every affected id as decimal JSON and could
        // occupy hundreds of MB. Convert them losslessly to consecutive-id ranges
        // before hosted writers start. A large saving gets a one-time VACUUM so
        // the physical database file shrinks immediately, not merely its freelist.
        var compacted = await TimeOffsetJournalCodec.CompactLegacyAsync(db);
        if (compacted.CompactedRows > 0)
        {
            Console.WriteLine(
                "Compacted {0} time-repair journals; logical saving {1:N0} bytes.",
                compacted.CompactedRows, compacted.BytesSaved);
            if (compacted.BytesSaved >= 16L * 1024 * 1024)
            {
                try
                {
                    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
                    await SqliteMaintenance.TryTruncateWalAsync(db);
                    await db.Database.ExecuteSqlRawAsync("VACUUM");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        "Journal compaction succeeded, but VACUUM could not reclaim the file yet: {0}",
                        ex.Message);
                }
            }
        }

        // Bound the retained physical WAL after maintenance/repair bursts.
        await SqliteMaintenance.TryTruncateWalAsync(db);
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint=1000");
    }

    /// <summary>
    /// EnsureCreated() only creates the schema for an empty database. For existing
    /// SQLite files, replay EF's create script for missing tables/indexes — a
    /// lightweight upgrade path without full EF migrations.
    /// </summary>
    private static async Task EnsureMissingTables(AppDbContext db)
    {
        var existingTables = db.Database.SqlQuery<string>($"""
            SELECT name AS Value FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
        """).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var script = db.Database.GenerateCreateScript();
        var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var statement in statements)
        {
            var sql = statement.Trim();
            if (sql.Length == 0) continue;

            var tableName = TryGetCreateTableName(sql);
            if (tableName != null)
            {
                if (existingTables.Contains(tableName)) continue;
                await db.Database.ExecuteSqlRawAsync(sql);
                existingTables.Add(tableName);
            }
        }
    }

    private static async Task EnsureMissingIndexes(AppDbContext db)
    {
        var existingIndexes = db.Database.SqlQuery<string>($"""
            SELECT name AS Value FROM sqlite_master WHERE type = 'index'
        """).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var script = db.Database.GenerateCreateScript();
        var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var statement in statements)
        {
            var sql = statement.Trim();
            if (!sql.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase)
                && !sql.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
                continue;

            var indexName = TryGetCreateIndexName(sql);
            if (indexName != null && existingIndexes.Contains(indexName)) continue;

            await db.Database.ExecuteSqlRawAsync(sql);
            if (indexName != null) existingIndexes.Add(indexName);
        }
    }

    private static string? TryGetCreateIndexName(string sql)
    {
        const string marker = "INDEX \"";
        var start = sql.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += marker.Length;
        var end = sql.IndexOf('"', start);
        return end > start ? sql[start..end] : null;
    }

    private static string? TryGetCreateTableName(string sql)
    {
        const string prefix = "CREATE TABLE \"";
        if (!sql.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var start = prefix.Length;
        var end = sql.IndexOf('"', start);
        return end > start ? sql[start..end] : null;
    }

    /// <summary>
    /// Migrates the MediaSessionRecords table from the old Timestamp column to
    /// the session-based StartTime/EndTime columns.
    /// </summary>
    internal static void MigrateMediaSessions(AppDbContext db)
    {
        var columns = db.Database.SqlQuery<ColumnInfo>($"""
            SELECT name FROM pragma_table_info('MediaSessionRecords')
        """).Select(c => c.Name).ToHashSet();

        if (columns.Contains("Timestamp") && !columns.Contains("StartTime"))
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE MediaSessionRecords RENAME COLUMN Timestamp TO StartTime");
        }

        if (!columns.Contains("EndTime"))
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE MediaSessionRecords ADD COLUMN EndTime TEXT NULL");
            // Only legacy point records need an inferred end. Running this on
            // every startup rewrote active sessions and zero-length SystemSleep
            // markers into artificial multi-second media records.
            db.Database.ExecuteSqlRaw(
                "UPDATE MediaSessionRecords SET EndTime = COALESCE(" +
                "(SELECT MIN(m2.StartTime) FROM MediaSessionRecords m2 WHERE m2.StartTime > MediaSessionRecords.StartTime), " +
                "StartTime) WHERE EndTime IS NULL");
        }
    }

    /// <summary>
    /// Adds FirstSeen column to ProcessIconMappings for icon versioning.
    /// Existing rows are backfilled with their LastSeen value.
    /// </summary>
    private static void MigrateProcessIconMappings(AppDbContext db)
    {
        var columns = db.Database.SqlQuery<ColumnInfo>($"""
            SELECT name FROM pragma_table_info('ProcessIconMappings')
        """).Select(c => c.Name).ToHashSet();

        if (!columns.Contains("FirstSeen"))
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE ProcessIconMappings ADD COLUMN FirstSeen TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'");
            db.Database.ExecuteSqlRaw(
                "UPDATE ProcessIconMappings SET FirstSeen = LastSeen WHERE FirstSeen = '0001-01-01T00:00:00'");
        }
    }

    /// <summary>
    /// Adds LastMonotonicMs and BootWallTime to Heartbeats for monotonic-clock time
    /// anomaly detection. Existing row backfilled with defaults (0 / MinValue,
    /// treated as "unset"): first heartbeat after upgrade skips the delta check
    /// and only records the current tick + boot anchor.
    /// </summary>
    private static void MigrateHeartbeatMonotonic(AppDbContext db)
    {
        var columns = db.Database.SqlQuery<ColumnInfo>($"""
            SELECT name FROM pragma_table_info('Heartbeats')
        """).Select(c => c.Name).ToHashSet();

        if (!columns.Contains("LastMonotonicMs"))
        {
            db.Database.ExecuteSqlRaw(
                $"ALTER TABLE Heartbeats ADD COLUMN LastMonotonicMs INTEGER NOT NULL DEFAULT 0");
        }

        if (!columns.Contains("BootWallTime"))
        {
            db.Database.ExecuteSqlRaw(
                $"ALTER TABLE Heartbeats ADD COLUMN BootWallTime TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'");
        }
    }

    /// <summary>
    /// 为 TimeAnomalies 增加 Direction 列("pre"/"post"/null,应用时解析的方向)。
    /// </summary>
    private static void MigrateTimeAnomalyDirection(AppDbContext db)
    {
        var columns = db.Database.SqlQuery<ColumnInfo>($"""
            SELECT name FROM pragma_table_info('TimeAnomalies')
        """).Select(c => c.Name).ToHashSet();

        if (!columns.Contains("Direction"))
            db.Database.ExecuteSqlRaw("ALTER TABLE TimeAnomalies ADD COLUMN Direction TEXT NULL");
    }

    private sealed class ColumnInfo { public string Name { get; set; } = string.Empty; }
}
