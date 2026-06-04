// taskmonitor114 — background monitoring with system tray and Web API.
//
// Architecture: main thread runs WinForms tray; background thread runs ASP.NET Core.
// Windows Service mode skips WinForms when !Environment.UserInteractive.
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;
using WinActivityTracker.Service.Native;

// STA thread is required for WinForms clipboard, drag-drop, and COM interop.
Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);

// DPI awareness must be set before any WinForms windows are created.
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

// --- Read port from settings.json early (before DI builds) ---
// The port must be known before WebApplication starts, but SettingsService is
// constructed inside builder.Build(). We read the file directly here.
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var trackerDir = Path.Combine(localAppData, "WinActivityTracker");
Directory.CreateDirectory(trackerDir);

// --- Single-instance guard ---
// Check for an existing taskmonitor114 process (excluding our own PID).
var myPid = Environment.ProcessId;
var existing = System.Diagnostics.Process.GetProcessesByName("taskmonitor114");
if (existing.Any(p => p.Id != myPid))
{
    MessageBox.Show("程序在运行中了。\n看看系统托盘不？",
        "别急", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}
var settingsPath = Path.Combine(trackerDir, "settings.json");
int apiPort = 5200;
if (File.Exists(settingsPath))
{
    try
    {
        var raw = File.ReadAllText(settingsPath);
        var clean = string.Join("\n", raw.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//")));
        using var doc = System.Text.Json.JsonDocument.Parse(clean);
        if (doc.RootElement.TryGetProperty("ApiPort", out var p) && p.TryGetInt32(out var v))
            apiPort = Math.Clamp(v, 1024, 65535);
    }
    catch { }
}

// --- Port conflict check ---
try
{
    using var test = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, apiPort);
    test.Start();
    test.Stop();
}
catch (System.Net.Sockets.SocketException)
{
    MessageBox.Show($"端口 {apiPort} 已被占用。\n请修改设置中的端口或关闭占用程序后重试。",
        "端口冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return;
}

// --- Redirect Console.Out BEFORE builder builds ---
// The ASP.NET Core console logger captures the TextWriter during Build().
// By swapping it now, ALL log output (trackers, API, EF Core) will flow
// through our ConsoleMirror, which the ConsoleWindow can display.
var mirror = new ConsoleMirror(Console.Out);
Console.SetOut(mirror);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(mirror);

// Kestrel: desktop tray app, not a public server. Low connection limits save memory.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxConcurrentConnections = 10);

// --- Database path: defaults to %LOCALAPPDATA% ---
var dbPath = Path.Combine(trackerDir, "activity.db");
Directory.CreateDirectory(trackerDir);

// Register BEFORE trackers — they depend on it via constructor injection
builder.Services.AddSingleton<SettingsService>();

// SQLite via EF Core. Shared cache reduces per-connection memory overhead.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared"));

// IdleDetector is singleton so all trackers share the same idle state
builder.Services.AddSingleton<IdleDetector>();
builder.Services.AddHostedService<WindowTracker>();
builder.Services.AddHostedService<ProcessTracker>();
builder.Services.AddHostedService<MediaSessionTracker>();
builder.Services.AddHostedService<HeartbeatService>();

// Windows Service support — enables sc.exe / PowerShell service management.
// When running as a service (detected automatically), the WinForms tray is skipped.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "taskmonitor114";
});

// CORS: allow local origins. Vue dev server runs on port 5000.
builder.Services.AddCors(c => c.AddDefaultPolicy(p =>
    p.WithOrigins($"http://localhost:{apiPort}", "http://localhost:5000").AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();

// --- Serve Vue SPA static files (production mode) ---
// In development, the Vue dev server runs separately on port 5000 with HMR.
// In production (or when tray icon opens :5200), the Service serves the built files.
// The CopyVueDist MSBuild target in the .csproj copies Web/wwwroot → output/wwwroot.
// wwwroot is in AppContext.BaseDirectory (copied by CopyVueDist MSBuild target).
// ContentRootPath differs during dotnet run, so we use a PhysicalFileProvider.
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var serveSpa = Directory.Exists(webRoot);
Microsoft.Extensions.FileProviders.PhysicalFileProvider? fp = null;
if (serveSpa)
{
    fp = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fp });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fp });
}

// EnsureCreatedAsync is safe for SQLite — creates tables if they don't exist.
// Does NOT handle migrations (this app uses EnsureCreated, not Migrate).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;PRAGMA cache_size=-2000");
}

// ===== API Endpoints =====

app.MapGet("/api/status", (SettingsService settings) =>
    Results.Ok(new { status = "running", trackingEnabled = settings.Settings.TrackingEnabled, timestamp = DateTime.UtcNow }));

// Settings endpoints — settings are loaded/saved to JSON file automatically by SettingsService
app.MapGet("/api/settings", (SettingsService settings) =>
    Results.Ok(settings.Settings));

app.MapPut("/api/settings", (TrackerSettings input, SettingsService settings) =>
{
    settings.Update(input);  // Enforces minimum values and saves to disk
    return Results.Ok(settings.Settings);
});

// Groups FocusChanges by process name for a single day.
// ?date= parameter is optional; defaults to today (server local time).
//
// TIMEZONE: targetDate represents a local calendar day. start/end are computed as
// midnight-to-midnight in LOCAL time, then converted to UTC for DB comparison.
// This ensures users see their full day's data regardless of timezone offset.
// Example: UTC+8 user on May 25 → start = May 24 16:00 UTC, end = May 25 16:00 UTC.
app.MapGet("/api/summary/today", async (string? date, AppDbContext db) =>
{
    var targetDate = date != null
        ? DateOnly.Parse(date)
        : DateOnly.FromDateTime(DateTime.Now);

    var localStart = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
    var localEnd = targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local);
    var start = localStart.ToUniversalTime();
    var end = localEnd.ToUniversalTime();

    // Query sleep/shutdown periods so we can exclude FocusChanges that fall within them.
    var offPeriods = await GetOffPeriods(db, start, end);

    var raw = await db.FocusChanges
        .Where(f => f.ProcessName != "__SystemSleep")
        .Where(f => f.Timestamp >= start && f.Timestamp <= end)
        .ToListAsync();

    // Exclude records whose timestamp falls within a sleep/shutdown window.
    var filtered = ExcludeOffPeriods(raw, offPeriods);

    var data = filtered
        .GroupBy(f => f.ProcessName)
        .Select(g => new
        {
            ProcessName = g.Key,
            TotalSeconds = g.Sum(f => f.DurationSeconds),
            SwitchCount = g.Count()
        })
        .OrderByDescending(x => x.TotalSeconds)
        .ToList();

    var adj = ComputeAdjustedSwitchCounts(filtered);

    var totalSleepSec = offPeriods.Sum(p => p.DurationSeconds);

    return Results.Ok(new
    {
        items = data.Select(d => new
        {
            d.ProcessName,
            d.TotalSeconds,
            SwitchCount = d.SwitchCount,
            AdjustedSwitchCount = adj.GetValueOrDefault(d.ProcessName, d.SwitchCount)
        }).OrderByDescending(x => x.TotalSeconds),
        totalSleepSeconds = totalSleepSec
    });
});

app.MapGet("/api/summary/range", async (DateTime from, DateTime to, AppDbContext db) =>
{
    var start = DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime();
    var end = DateTime.SpecifyKind(to, DateTimeKind.Local).ToUniversalTime();

    var offPeriods = await GetOffPeriods(db, start, end);

    var raw = await db.FocusChanges
        .Where(f => f.ProcessName != "__SystemSleep")
        .Where(f => f.Timestamp >= start && f.Timestamp <= end)
        .ToListAsync();

    var filtered = ExcludeOffPeriods(raw, offPeriods);

    var data = filtered
        .GroupBy(f => f.ProcessName)
        .Select(g => new
        {
            ProcessName = g.Key,
            TotalSeconds = g.Sum(f => f.DurationSeconds),
            SwitchCount = g.Count()
        })
        .OrderByDescending(x => x.TotalSeconds)
        .ToList();

    var adj = ComputeAdjustedSwitchCounts(filtered);

    var totalSleepSec = offPeriods.Sum(p => p.DurationSeconds);

    return Results.Ok(new
    {
        items = data.Select(d => new
        {
            d.ProcessName,
            d.TotalSeconds,
            SwitchCount = d.SwitchCount,
            AdjustedSwitchCount = adj.GetValueOrDefault(d.ProcessName, d.SwitchCount)
        }).OrderByDescending(x => x.TotalSeconds),
        totalSleepSeconds = totalSleepSec
    });
});

// Returns sleep/shutdown periods (SystemEvents) in the given UTC range.
static async Task<List<(DateTime Start, DateTime End, double DurationSeconds)>> GetOffPeriods(AppDbContext db, DateTime start, DateTime end)
{
    var events = await db.SystemEvents
        .Where(e => (e.EventType == "Sleep" || e.EventType == "Shutdown")
            && e.Timestamp >= start && e.Timestamp <= end)
        .ToListAsync();

    return events
        .Select(e => (Start: e.Timestamp, End: e.Timestamp.AddSeconds(e.DurationSeconds), e.DurationSeconds))
        .ToList();
}

// Excludes FocusChanges whose timestamp falls within any off period.
static List<FocusChange> ExcludeOffPeriods(List<FocusChange> records, List<(DateTime Start, DateTime End, double DurationSeconds)> offPeriods)
{
    if (offPeriods.Count == 0) return records;
    return records
        .Where(f => !offPeriods.Any(p => f.Timestamp >= p.Start && f.Timestamp < p.End))
        .ToList();
}

// Computes merge-same-process switch counts from an in-memory list.
// Only counts transitions where ProcessName differs from the previous row.
static Dictionary<string, int> ComputeAdjustedSwitchCounts(List<FocusChange> records)
{
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    string? prev = null;
    foreach (var r in records.OrderBy(r => r.Timestamp))
    {
        if (r.ProcessName != prev)
            counts[r.ProcessName] = counts.GetValueOrDefault(r.ProcessName) + 1;
        prev = r.ProcessName;
    }
    return counts;
}

// Returns the LIVE list of visible windows — does not query the database.
// Useful for the Timeline page's real-time panel and the native StatusWindow.
app.MapGet("/api/windows/current", (SettingsService settings) =>
{
    var excluded = settings.Settings.ExcludedProcesses;
    var windows = WindowTracker.EnumerateVisibleWindows();
    return Results.Ok(windows
        .Where(w => !excluded.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase))
        .Select(w => new
        {
            w.ProcessName,
            w.Title,
            w.IsFocused
        }));
});

// Time-ordered focus change records for the timeline view.
// Default range: last hour.
app.MapGet("/api/windows/timeline", async (DateTime? from, DateTime? to, AppDbContext db) =>
{
    var start = from.HasValue
        ? DateTime.SpecifyKind(from.Value, DateTimeKind.Local).ToUniversalTime()
        : DateTime.UtcNow.AddHours(-1);
    var end = to.HasValue
        ? DateTime.SpecifyKind(to.Value, DateTimeKind.Local).ToUniversalTime()
        : DateTime.UtcNow;

    var data = await db.FocusChanges
        .Where(f => f.Timestamp >= start && f.Timestamp <= end)
        .OrderBy(f => f.Timestamp)
        .Select(f => new
        {
            f.Timestamp,
            f.ProcessName,
            f.WindowTitle,
            f.DurationSeconds
        })
        .ToListAsync();

    return Results.Ok(data);
});

// Returns currently-running background processes (sessions with no EndTime).
app.MapGet("/api/processes/snapshot", async (AppDbContext db) =>
{
    var data = await db.ProcessSessions
        .Where(p => p.EndTime == null)
        .Select(p => new { p.ProcessName, p.ProcessId })
        .Distinct()
        .ToListAsync();

    return Results.Ok(data);
});

// Serve title_rules.json so the frontend can normalize window titles
app.MapGet("/api/title-rules", () =>
{
    var n = new TitleNormalizer();
    return Results.Ok(n.GetRules());
});

app.MapGet("/api/media/history", async (int? limit, AppDbContext db) =>
{
    var data = await db.MediaSessionRecords
        .OrderByDescending(m => m.Timestamp)
        .Take(limit ?? 20)
        .Select(m => new
        {
            m.Timestamp,
            m.AppName,
            m.Title,
            m.Artist,
            m.PlaybackStatus
        })
        .ToListAsync();

    return Results.Ok(data);
});

// Database statistics — single combined query instead of 6 round trips.
// If the oldest record is the same as today, newRecordsPerDay will be artificially high.
app.MapGet("/api/db/stats", async (AppDbContext db) =>
{
    var now = DateTime.UtcNow;

    // Single SQL query gathers all counts + oldest timestamp in one round trip.
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
        newRecordsPerDay = Math.Round(stats.FocusCount / Math.Max(1, (now - (stats.OldestRecord ?? now)).TotalDays), 1)
    });
});

// Deletes records older than retentionDays from ALL tables, then runs VACUUM to reclaim disk.
// VACUUM copies the database, so it can be slow on large files (>100MB).
// Default retention from settings.DataRetentionDays; override with ?days= parameter.
app.MapPost("/api/db/cleanup", async (int? days, AppDbContext db, SettingsService settings) =>
{
    var retention = days ?? settings.Settings.DataRetentionDays;
    var cutoff = DateTime.UtcNow.AddDays(-retention);

    var deletedFocus = await db.FocusChanges.Where(f => f.Timestamp < cutoff).ExecuteDeleteAsync();
    var deletedWindows = await db.WindowSnapshots.Where(w => w.Timestamp < cutoff).ExecuteDeleteAsync();
    var deletedSessions = await db.WindowSessions.Where(w => w.OpenTime < cutoff).ExecuteDeleteAsync();
    var deletedProcesses = await db.ProcessSnapshots.Where(p => p.Timestamp < cutoff).ExecuteDeleteAsync();
    var deletedProcSessions = await db.ProcessSessions.Where(p => p.StartTime < cutoff).ExecuteDeleteAsync();
    var deletedMedia = await db.MediaSessionRecords.Where(m => m.Timestamp < cutoff).ExecuteDeleteAsync();
    var deletedSystemEvents = await db.SystemEvents.Where(e => e.Timestamp < cutoff).ExecuteDeleteAsync();

    await db.Database.ExecuteSqlRawAsync("VACUUM");

    return Results.Ok(new
    {
        retentionDays = retention,
        cutoff = cutoff,
        deleted = new { focusChanges = deletedFocus, windowSnapshots = deletedWindows, windowSessions = deletedSessions, processSnapshots = deletedProcesses, processSessions = deletedProcSessions, mediaRecords = deletedMedia, systemEvents = deletedSystemEvents }
    });
});

// Deletes ALL records from all tables and VACUUMs.
// Requires ?confirm=true to prevent accidental data loss.
// Database schema is preserved; only rows are deleted.
app.MapPost("/api/db/reset", async (bool? confirm, AppDbContext db) =>
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
        deleted = new { focusChanges = deletedFocus, windowSnapshots = deletedWindows, windowSessions = deletedSessions, processSnapshots = deletedProcesses, processSessions = deletedProcSessions, mediaRecords = deletedMedia, systemEvents = deletedSystemEvents }
    });
});

// SPA fallback — must be AFTER all API routes so it only catches unmatched requests.
// Returns index.html for routes like /history, /timeline, /settings so Vue Router handles them.
if (serveSpa && fp != null)
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fp });

app.Urls.Add($"http://localhost:{apiPort}");

// ===== Startup: dual-thread or service mode =====

if (!Environment.UserInteractive)
{
    // Running as a Windows Service — no GUI, web host on main thread.
    Console.WriteLine($"WinActivityTracker starting as Windows Service on http://localhost:{apiPort}");
    await app.RunAsync();
    return;
}

// Interactive mode: web host on background thread, WinForms tray on main thread.
// Application.Run() blocks until Application.Exit() is called (from tray "退出" menu).
Console.WriteLine($"WinActivityTracker starting on http://localhost:{apiPort}");

var webTask = Task.Run(() => app.RunAsync());

// Ensure the web host has started before showing the tray
await Task.Delay(500);

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
// --autostart: don't pop up StatusWindow on startup (used by registry auto-start)
var silent = args.Any(a => a is "--autostart" or "--silent");
Application.Run(new TrayApplicationContext(app.Services, apiPort, autoShowStatus: !silent));

// Record clean shutdown so HeartbeatService can distinguish shutdown from sleep on next startup.
try
{
    using var shutdownScope = app.Services.CreateScope();
    var shutdownDb = shutdownScope.ServiceProvider.GetRequiredService<AppDbContext>();
    shutdownDb.SystemEvents.Add(new SystemEvent
    {
        EventType = "Shutdown",
        Timestamp = DateTime.UtcNow
    });
    await shutdownDb.SaveChangesAsync();
}
catch (Exception ex) { Console.WriteLine($"Failed to record shutdown: {ex.Message}"); }

// When Application.Run returns (user clicked Exit), signal the web host to stop.
await app.StopAsync();
try { await webTask; } catch (OperationCanceledException) { }

// Type for the /api/db/stats combined SQL query.
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

