// taskmonitor114 — background monitoring with system tray and on-demand Web API.
//
// Architecture: Host (trackers) runs always. DashboardServer (Kestrel + Vue SPA)
// starts on-demand when the user opens the dashboard and stops after idle timeout.
// PortWatcher listens when Kestrel is down and wakes it on incoming connections,
// so a frozen-tab refresh never gets "connection refused".
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;
using WinActivityTracker.Service;
using WinActivityTracker.Service.Native;

if (Environment.UserInteractive)
{
    Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.EnableVisualStyles();
}

var silent = args.Any(a => a is "--autostart" or "--silent");
var isTesting = Environment.GetEnvironmentVariable("WTA_TESTING") == "1";

ThreadPool.SetMinThreads(1, 1);

var appPaths = new AppPaths();
appPaths.MigrateIfNeeded();

var lang = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh")
    ? "zh-CN" : "en-US";
var i18n = new I18nService(lang);

var settingsPath = Path.Combine(appPaths.ConfigDir, "settings.json");
var apiPort = ProgramStartup.ParsePortFromSettings(settingsPath);

// ===== Pre-flight checks (skipped in test mode) =====
if (!isTesting)
{
    if (!ProgramStartup.EnsureSingleInstance(appPaths.DataDir, silent)) return;
    if (!ProgramStartup.IsPortAvailable(apiPort, silent)) return;
}

var mirror = new ConsoleMirror(Console.Out);
Console.SetOut(mirror);

var dbPath = Environment.GetEnvironmentVariable("WTA_DB_PATH")
    ?? Path.Combine(appPaths.DataDir, "activity.db");

// ===== Host — trackers run as HostedServices, always active =====
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton(mirror);
builder.Services.AddSingleton(appPaths);
builder.Services.AddSingleton(i18n);
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<TagService>();
builder.Services.AddSingleton<TitleNormalizer>();
builder.Services.AddSingleton<ProcessNameCache>();
builder.Services.AddSingleton<IconService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared"));
builder.Services.AddSingleton<IdleDetector>();
builder.Services.AddSingleton<WriteQueue>();
builder.Services.AddSingleton<SystemPressure>();

// Trackers (BackgroundService)
builder.Services.AddHostedService(sp => sp.GetRequiredService<WriteQueue>());
builder.Services.AddHostedService<WindowTracker>();
builder.Services.AddHostedService<ProcessTracker>();
builder.Services.AddHostedService<MediaSessionTracker>();
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.AddSingleton<IconCacheService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IconCacheService>());

// On-demand web server
builder.Services.AddSingleton<PortWatcher>(sp => new PortWatcher(apiPort, async () =>
{
    var dashboard = sp.GetRequiredService<DashboardServer>();
    await dashboard.StartAsync();
}));
builder.Services.AddSingleton<DashboardServer>(sp =>
{
    var portWatcher = sp.GetRequiredService<PortWatcher>();
    return new DashboardServer(sp, dbPath, apiPort, portWatcher);
});

builder.Services.AddWindowsService(options => { options.ServiceName = "taskmonitor114"; });

var host = builder.Build();

// ===== DB init =====
await ProgramStartup.InitializeDatabase(host.Services);

// ===== Process cache init =====
var processCache = host.Services.GetRequiredService<ProcessNameCache>();
processCache.RefreshAll();
// WMI requires admin privileges; skip on non-elevated to avoid loading WMI COM (~10-15MB native).
var isAdmin = new System.Security.Principal.WindowsPrincipal(
    System.Security.Principal.WindowsIdentity.GetCurrent())
    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
if (isAdmin)
    processCache.StartWmiWatch();

// ===== Start trackers =====
await host.StartAsync();

var dashboard = host.Services.GetRequiredService<DashboardServer>();

// ===== Test mode: start DashboardServer for integration tests, skip WinForms =====
if (isTesting)
{
    await dashboard.StartAsync();
    await host.WaitForShutdownAsync();
    return;
}

// ===== Windows Service mode: start Kestrel immediately =====
if (!Environment.UserInteractive)
{
    Console.WriteLine($"WinActivityTracker starting as Windows Service on http://localhost:{apiPort}");
    await dashboard.StartAsync();
    await host.WaitForShutdownAsync();
    ProgramStartup.DeletePidFile(appPaths.DataDir);
    return;
}

// ===== Interactive mode: PortWatcher owns the port until dashboard is opened =====
host.Services.GetRequiredService<PortWatcher>().Start();

Console.WriteLine($"WinActivityTracker starting on http://localhost:{apiPort}");

Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new TrayApplicationContext(host.Services, dashboard, autoShowStatus: !silent));

// ===== Clean shutdown =====
try
{
    using var shutdownScope = host.Services.CreateScope();
    var shutdownDb = shutdownScope.ServiceProvider.GetRequiredService<AppDbContext>();
    shutdownDb.SystemEvents.Add(new SystemEvent
    {
        EventType = SystemEventTypes.Shutdown,
        Timestamp = DateTime.UtcNow
    });
    await shutdownDb.SaveChangesAsync();
}
catch (Exception ex) { Console.WriteLine($"Failed to record shutdown: {ex.Message}"); }

ProgramStartup.DeletePidFile(appPaths.DataDir);

if (dashboard.IsRunning)
    await dashboard.StopAsync();

await host.StopAsync();
