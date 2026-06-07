// taskmonitor114 — background monitoring with system tray and Web API.
//
// Architecture: main thread runs WinForms tray; background thread runs ASP.NET Core.
// Windows Service mode skips WinForms when !Environment.UserInteractive.
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;
using WinActivityTracker.Service.Api;
using WinActivityTracker.Service.Native;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();

// ===== Pre-flight: single-instance guard, port, console redirect =====
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var trackerDir = Path.Combine(localAppData, "WinActivityTracker");
Directory.CreateDirectory(trackerDir);

var myPid = Environment.ProcessId;
var existing = System.Diagnostics.Process.GetProcessesByName("taskmonitor114");
if (existing.Any(p => p.Id != myPid))
{
    MessageBox.Show("程序在运行中了。\n看看系统托盘不？",
        "別急", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

int apiPort = 5200;
var settingsPath = Path.Combine(trackerDir, "settings.json");
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
    catch (System.Text.Json.JsonException ex)
    {
        Console.WriteLine($"Warning: failed to parse settings.json for port — {ex.Message}. Using default {apiPort}.");
    }
}

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

var mirror = new ConsoleMirror(Console.Out);
Console.SetOut(mirror);

// ===== DI setup =====
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(mirror);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxConcurrentConnections = 10);

var dbPath = Path.Combine(trackerDir, "activity.db");

builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ProcessNameCache>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared"));
builder.Services.AddSingleton<IdleDetector>();
builder.Services.AddSingleton<WriteQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WriteQueue>());
builder.Services.AddHostedService<WindowTracker>();
builder.Services.AddHostedService<ProcessTracker>();
builder.Services.AddHostedService<MediaSessionTracker>();
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.AddWindowsService(options => { options.ServiceName = "taskmonitor114"; });
builder.Services.AddCors(c => c.AddDefaultPolicy(p =>
    p.WithOrigins($"http://localhost:{apiPort}", "http://localhost:5000").AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

// ===== SPA static files (production mode) =====
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var serveSpa = Directory.Exists(webRoot);
Microsoft.Extensions.FileProviders.PhysicalFileProvider? fp = null;
if (serveSpa)
{
    fp = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fp });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fp });
}

// ===== DB init =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;PRAGMA cache_size=-2000");
    MigrateMediaSessions(db);
}

// ===== API endpoints =====
app.MapAdminEndpoints();
app.MapFocusEndpoints();
app.MapMediaEndpoints();
app.MapWindowEndpoints();

if (serveSpa && fp != null)
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fp });

app.Urls.Add($"http://localhost:{apiPort}");

// ===== Startup: dual-thread or service mode =====
if (!Environment.UserInteractive)
{
    Console.WriteLine($"WinActivityTracker starting as Windows Service on http://localhost:{apiPort}");
    await app.RunAsync();
    return;
}

Console.WriteLine($"WinActivityTracker starting on http://localhost:{apiPort}");
var webTask = Task.Run(() => app.RunAsync());

// Poll /api/status until the web host is actually ready, up to 300ms.
using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(50) };
var deadline = DateTime.UtcNow.AddMilliseconds(300);
var ready = false;
while (DateTime.UtcNow < deadline)
{
    try
    {
        var resp = await http.GetAsync($"http://localhost:{apiPort}/api/status");
        if (resp.IsSuccessStatusCode) { ready = true; break; }
    }
    catch { /* not ready */ }
    await Task.Delay(TimeSpan.FromMilliseconds(3));
}

if (!ready)
{
    Console.Error.WriteLine("WARNING: API did not respond within 300ms — startup may be slow.");
    MessageBox.Show("API 服务启动缓慢，可能存在问题。\n请检查端口占用或系统资源。",
        "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

Application.SetCompatibleTextRenderingDefault(false);
var silent = args.Any(a => a is "--autostart" or "--silent");
Application.Run(new TrayApplicationContext(app.Services, apiPort, autoShowStatus: !silent));

// Record clean shutdown.
try
{
    using var shutdownScope = app.Services.CreateScope();
    var shutdownDb = shutdownScope.ServiceProvider.GetRequiredService<AppDbContext>();
    shutdownDb.SystemEvents.Add(new SystemEvent
    {
        EventType = SystemEventTypes.Shutdown,
        Timestamp = DateTime.UtcNow
    });
    await shutdownDb.SaveChangesAsync();
}
catch (Exception ex) { Console.WriteLine($"Failed to record shutdown: {ex.Message}"); }

await app.StopAsync();
try { await webTask; } catch (OperationCanceledException) { }

// ===== Internal helpers =====
static void MigrateMediaSessions(AppDbContext db)
{
    // Check current schema so we don't produce noisy EF Core failure logs.
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

internal sealed class ColumnInfo { public string Name { get; set; } = string.Empty; }
