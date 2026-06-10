// Pre-flight checks for taskmonitor114 startup.
// Extracted from Program.cs to keep the main entry point readable.
using WinActivityTracker.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace WinActivityTracker.Service;

internal static class ProgramStartup
{
    /// <summary>
    /// Checks if another taskmonitor114 process is already running.
    /// Returns true if startup should proceed, false if it should exit.
    /// </summary>
    public static bool EnsureSingleInstance(bool silent)
    {
        var myPid = Environment.ProcessId;
        var existing = System.Diagnostics.Process.GetProcessesByName("taskmonitor114");
        if (!existing.Any(p => p.Id != myPid)) return true;

        if (!silent)
            MessageBox.Show("程序在运行中了。\n看看系统托盘不？",
                "別急", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Console.Error.WriteLine("Another instance is already running.");
        return false;
    }

    /// <summary>
    /// Reads ApiPort from settings.json (stripping // comment lines).
    /// Falls back to 5200 if the file doesn't exist or can't be parsed.
    /// </summary>
    public static int ParsePortFromSettings(string settingsPath, int defaultPort = 5200)
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
                MessageBox.Show($"端口 {port} 已被占用。\n请修改设置中的端口或关闭占用程序后重试。",
                    "端口冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;PRAGMA cache_size=-2000");
        MigrateMediaSessions(db);
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

    private sealed class ColumnInfo { public string Name { get; set; } = string.Empty; }
}
