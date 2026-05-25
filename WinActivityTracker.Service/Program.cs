// WinActivityTracker.Service — the backend monitoring process with native system tray.
//
// Dual-thread architecture:
//   Main thread (STA):  WinForms Application.Run() — system tray icon + status window
//   Background thread:  ASP.NET Core WebApplication — REST API + trackers
//
// Both threads share the same DI container (IServiceProvider), so the tray/status UI
// can access SettingsService, and the API can access tracker data.
//
// When running as a Windows Service (detected via !Environment.UserInteractive),
// the WinForms UI is skipped entirely — only the web host runs.
//
// API endpoints:
//   GET  /api/status              — health check (+ trackingEnabled)
//   GET  /api/settings            — read current configuration
//   PUT  /api/settings            — update configuration (body: TrackerSettings JSON)
//   GET  /api/summary/today       — today's focus time per process (?date=yyyy-MM-dd)
//   GET  /api/summary/range       — focus time over a date range (?from=&to=)
//   GET  /api/windows/current     — live list of visible windows (real-time, not from DB)
//   GET  /api/windows/timeline    — focus change history (?from=&to=)
//   GET  /api/processes/snapshot  — latest background process snapshot
//   GET  /api/media/history       — recent media playback records (?limit=)
//   GET  /api/db/stats            — database row counts and age
//   POST /api/db/cleanup          — delete old records and VACUUM (?days=)
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;
using WinActivityTracker.Service.Native;

// STA thread is required for WinForms clipboard, drag-drop, and COM interop.
// Must be set before any WinForms controls are created.
Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);

// --- Read port from settings.json early (before DI builds) ---
// The port must be known before WebApplication starts, but SettingsService is
// constructed inside builder.Build(). We read the file directly here.
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var trackerDir = Path.Combine(localAppData, "WinActivityTracker");
Directory.CreateDirectory(trackerDir);
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

// --- Redirect Console.Out BEFORE builder builds ---
// The ASP.NET Core console logger captures the TextWriter during Build().
// By swapping it now, ALL log output (trackers, API, EF Core) will flow
// through our ConsoleMirror, which the ConsoleWindow can display.
var mirror = new ConsoleMirror(Console.Out);
Console.SetOut(mirror);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(mirror);

// --- Database path: defaults to %LOCALAPPDATA% ---
var dbPath = Path.Combine(trackerDir, "activity.db");
Directory.CreateDirectory(trackerDir);

// Register BEFORE trackers — they depend on it via constructor injection
builder.Services.AddSingleton<SettingsService>();

// SQLite via EF Core. Connection string is the file path.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// IdleDetector is singleton so all trackers share the same idle state
builder.Services.AddSingleton<IdleDetector>();
builder.Services.AddHostedService<WindowTracker>();
builder.Services.AddHostedService<ProcessTracker>();
builder.Services.AddHostedService<MediaSessionTracker>();

// Windows Service support — enables sc.exe / PowerShell service management.
// When running as a service (detected automatically), the WinForms tray is skipped.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "WinActivityTracker";
});

// CORS: allow all origins in development. The Vue dev server runs on port 5000.
builder.Services.AddCors(c => c.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();

// --- Serve Vue SPA static files (production mode) ---
// In development, the Vue dev server runs separately on port 5000 with HMR.
// In production (or when tray icon opens :5200), the Service serves the built files.
// The CopyVueDist MSBuild target in the .csproj copies Web/wwwroot → output/wwwroot.
// wwwroot is copied to the output directory by the CopyVueDist MSBuild target.
// During dotnet run, ContentRootPath = project dir, but wwwroot is in the bin output dir.
// We configure a PhysicalFileProvider so UseStaticFiles uses the correct path.
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

    var data = await db.FocusChanges
        .Where(f => f.Timestamp >= start && f.Timestamp <= end)
        .GroupBy(f => f.ProcessName)
        .Select(g => new
        {
            ProcessName = g.Key,
            TotalSeconds = g.Sum(f => f.DurationSeconds),
            SwitchCount = g.Count()
        })
        .OrderByDescending(x => x.TotalSeconds)
        .ToListAsync();

    return Results.Ok(data);
});

app.MapGet("/api/summary/range", async (DateTime from, DateTime to, AppDbContext db) =>
{
    var data = await db.FocusChanges
        .Where(f => f.Timestamp >= from && f.Timestamp <= to)
        .GroupBy(f => f.ProcessName)
        .Select(g => new
        {
            ProcessName = g.Key,
            TotalSeconds = g.Sum(f => f.DurationSeconds),
            SwitchCount = g.Count()
        })
        .OrderByDescending(x => x.TotalSeconds)
        .ToListAsync();

    return Results.Ok(data);
});

// Returns the LIVE list of visible windows — does not query the database.
// Useful for the Timeline page's real-time panel and the native StatusWindow.
app.MapGet("/api/windows/current", () =>
{
    var windows = WindowTracker.EnumerateVisibleWindows();
    return Results.Ok(windows.Select(w => new
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
    var start = from ?? DateTime.UtcNow.AddHours(-1);
    var end = to ?? DateTime.UtcNow;

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

// Returns the LATEST batch of background process snapshots.
// Each poll cycle creates many rows with the same timestamp; DISTINCT deduplicates PIDs.
app.MapGet("/api/processes/snapshot", async (AppDbContext db) =>
{
    var latest = await db.ProcessSnapshots
        .OrderByDescending(p => p.Timestamp)
        .Select(p => p.Timestamp)
        .FirstOrDefaultAsync();

    if (latest == default)
        return Results.Ok(Array.Empty<object>());

    var data = await db.ProcessSnapshots
        .Where(p => p.Timestamp == latest)
        .Select(p => new { p.ProcessName, p.ProcessId })
        .Distinct()
        .ToListAsync();

    return Results.Ok(data);
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

// Database statistics — useful for monitoring growth.
// If the oldest record is the same as today, newRecordsPerDay will be artificially high.
app.MapGet("/api/db/stats", async (AppDbContext db) =>
{
    var now = DateTime.UtcNow;

    var focusCount = await db.FocusChanges.CountAsync();
    var windowCount = await db.WindowSnapshots.CountAsync();
    var processCount = await db.ProcessSnapshots.CountAsync();
    var mediaCount = await db.MediaSessionRecords.CountAsync();

    var oldest = await db.FocusChanges
        .OrderBy(f => f.Timestamp)
        .Select(f => (DateTime?)f.Timestamp)
        .FirstOrDefaultAsync();

    return Results.Ok(new
    {
        focusChanges = focusCount,
        windowSnapshots = windowCount,
        processSnapshots = processCount,
        mediaRecords = mediaCount,
        oldestRecord = oldest,
        newRecordsPerDay = Math.Round(focusCount / Math.Max(1, (now - (oldest ?? now)).TotalDays), 1)
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
    var deletedProcesses = await db.ProcessSnapshots.Where(p => p.Timestamp < cutoff).ExecuteDeleteAsync();
    var deletedMedia = await db.MediaSessionRecords.Where(m => m.Timestamp < cutoff).ExecuteDeleteAsync();

    await db.Database.ExecuteSqlRawAsync("VACUUM");

    return Results.Ok(new
    {
        retentionDays = retention,
        cutoff = cutoff,
        deleted = new { focusChanges = deletedFocus, windowSnapshots = deletedWindows, processSnapshots = deletedProcesses, mediaRecords = deletedMedia }
    });
});

// Nuclear option: deletes ALL records from all tables and VACUUMs.
// Requires ?confirm=true as a safety check — prevents accidental data loss.
// The database schema is preserved; only rows are deleted.
app.MapPost("/api/db/reset", async (bool? confirm, AppDbContext db) =>
{
    if (confirm != true)
        return Results.BadRequest(new { error = "Set ?confirm=true to delete all data. This cannot be undone." });

    var deletedFocus = await db.FocusChanges.ExecuteDeleteAsync();
    var deletedWindows = await db.WindowSnapshots.ExecuteDeleteAsync();
    var deletedProcesses = await db.ProcessSnapshots.ExecuteDeleteAsync();
    var deletedMedia = await db.MediaSessionRecords.ExecuteDeleteAsync();

    await db.Database.ExecuteSqlRawAsync("VACUUM");

    return Results.Ok(new
    {
        message = "All data deleted. Database schema preserved.",
        deleted = new { focusChanges = deletedFocus, windowSnapshots = deletedWindows, processSnapshots = deletedProcesses, mediaRecords = deletedMedia }
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

// PerMonitorV2 enables per-monitor DPI scaling — prevents blurry text on high-DPI displays.
// Must be called before any WinForms controls are created (before EnableVisualStyles).
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new TrayApplicationContext(app.Services, apiPort));

// When Application.Run returns (user clicked Exit), signal the web host to stop.
await app.StopAsync();
try { await webTask; } catch (OperationCanceledException) { }
