// Pre-flight checks for taskmonitor114 startup.
// Extracted from Program.cs to keep the main entry point readable.
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace WinActivityTracker.Service;

internal static class ProgramStartup
{
    /// <summary>
    /// Single-instance guard via PID file in DataDir. Writes the current PID
    /// to taskmonitor114.pid on success. Call DeletePidFile() on shutdown.
    /// Returns true if startup should proceed, false if another instance is running.
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
                        // Stale lock file — process no longer exists.
                        File.Delete(pidFile);
                    }
                }
            }
            catch
            {
                // Corrupted or inaccessible lock file — delete and continue.
                try { File.Delete(pidFile); } catch { }
            }
        }

        try
        {
            File.WriteAllText(pidFile, myPid.ToString());
        }
        catch
        {
            // Can't write PID file — not fatal, continue without guard.
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
        await EnsureMissingIndexes(db);
    }

    /// <summary>
    /// EnsureCreated() only creates schema when the database is empty. For existing
    /// SQLite files, replay EF's create script for tables/indexes that are missing.
    /// This keeps lightweight upgrade behavior without full EF migrations.
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
        var script = db.Database.GenerateCreateScript();
        var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var statement in statements)
        {
            var sql = statement.Trim();
            if (!sql.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase)
                && !sql.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
                continue;

            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch { }
        }
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
    private static void MigrateMediaSessions(AppDbContext db)
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
        }

        db.Database.ExecuteSqlRaw(
            "UPDATE MediaSessionRecords SET EndTime = COALESCE(" +
            "(SELECT MIN(m2.StartTime) FROM MediaSessionRecords m2 WHERE m2.StartTime > MediaSessionRecords.StartTime), " +
            "StartTime) WHERE EndTime IS NULL OR EndTime = StartTime");
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

    private sealed class ColumnInfo { public string Name { get; set; } = string.Empty; }
}
