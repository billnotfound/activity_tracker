// taskmonitor114 — background monitoring with system tray and Web API.
//
// Architecture: main thread runs WinForms tray; background thread runs ASP.NET Core.
// Windows Service mode skips WinForms when !Environment.UserInteractive.
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;
using WinActivityTracker.Service;
using WinActivityTracker.Service.Api;
using WinActivityTracker.Service.Native;

if (Environment.UserInteractive)
{
    Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.EnableVisualStyles();
}

var silent = args.Any(a => a is "--autostart" or "--silent");
var isTesting = Environment.GetEnvironmentVariable("WTA_TESTING") == "1";

var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var trackerDir = Path.Combine(localAppData, "WinActivityTracker");
Directory.CreateDirectory(trackerDir);

var settingsPath = Path.Combine(trackerDir, "settings.json");
var apiPort = ProgramStartup.ParsePortFromSettings(settingsPath);

// ===== Pre-flight checks (skipped in test mode) =====
if (!isTesting)
{
    if (!ProgramStartup.EnsureSingleInstance(silent)) return;
    if (!ProgramStartup.IsPortAvailable(apiPort, silent)) return;
}

var mirror = new ConsoleMirror(Console.Out);
Console.SetOut(mirror);

// ===== DI setup =====
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(mirror);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxConcurrentConnections = 10);

var dbPath = Environment.GetEnvironmentVariable("WTA_DB_PATH")
    ?? Path.Combine(trackerDir, "activity.db");

builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<TagService>();
builder.Services.AddSingleton<TitleNormalizer>();
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
await ProgramStartup.InitializeDatabase(app.Services);

// ===== API endpoints =====
app.MapAdminEndpoints();
app.MapFocusEndpoints();
app.MapMediaEndpoints();
app.MapWindowEndpoints();
app.MapTagEndpoints();

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

// Poll /api/status until Kestrel is ready.
using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(100) };
var deadline = DateTime.UtcNow.AddMilliseconds(500);
var ready = false;
while (DateTime.UtcNow < deadline)
{
    try
    {
        var resp = await http.GetAsync($"http://127.0.0.1:{apiPort}/api/status");
        if (resp.IsSuccessStatusCode) { ready = true; break; }
    }
    catch { /* not ready */ }
    await Task.Delay(TimeSpan.FromMilliseconds(3));
}

if (!ready)
{
    Console.Error.WriteLine("WARNING: API did not respond within 500ms — startup may be slow.");
    if (!silent)
        MessageBox.Show("API 服务启动缓慢，可能存在问题。\n请检查端口占用或系统资源。",
            "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

Application.SetCompatibleTextRenderingDefault(false);
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
