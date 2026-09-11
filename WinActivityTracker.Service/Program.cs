// taskmonitor114 — background monitoring with system tray and on-demand Web API.
//
// Architecture: Host (trackers) always runs. DashboardServer (Kestrel + Vue SPA)
// starts on-demand when the dashboard opens and stops after idle timeout.
// PortWatcher owns the port while Kestrel is down; on incoming connections it
// wakes Kestrel so a frozen-tab refresh never gets "connection refused".
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

// Toast 激活进程：COM 激活器回调在本进程内执行，经命名管道转发给主实例后退出。
// 必须在 EnsureSingleInstance 之前——激活进程不应弹单实例框。
// 无论转发是否成功都退出；激活进程不得回退为完整追踪实例。
if (args.Any(a => a is "-Embedding"))
{
    try
    {
        await ToastActivationClient.SendAsync();
    }
    catch { /* 主实例不在或管道不可用 */ }
    return;
}

// 服务模式 helper：LocalSystem 服务用 CreateProcessAsUser 把同 exe 的 helper 分支
// 投递到交互会话。只弹 toast 并在点击时开面板；不启动追踪器、不做单实例检查
//（在 Pre-flight 之前）。必须在 AppPaths/MigrateIfNeeded 之前：helper 在用户桌面侧，
// 重解析路径会读到交互用户的 DB/配置目录而非服务侧的——异常 ID/偏移/端口
// 全部由服务侧经命令行传入。
if (ToastHelper.IsHelperMode(args))
{
    ToastHelper.Run(args);
    return;
}

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
    options.UseSqlite(SqliteConnectionStrings.Build(dbPath)));
builder.Services.AddSingleton<IdleDetector>();
builder.Services.AddSingleton<WriteQueue>();
builder.Services.AddSingleton<SystemPressure>();

// Trackers (BackgroundService)
builder.Services.AddHostedService(sp => sp.GetRequiredService<WriteQueue>());
builder.Services.AddHostedService<WindowTracker>();
builder.Services.AddHostedService<ProcessTracker>();
builder.Services.AddHostedService<MediaSessionTracker>();
// Time anomaly detection: event-log reader + NTP scheduler + lifecycle service.
// AddHostedService<T> 只注册 IHostedService→T 映射，不注册具体类型——必须先
// AddSingleton，再用 GetRequiredService 注册 hosted，构造注入/工厂解析才能拿到同一实例。
builder.Services.AddSingleton<SystemTimeChangeReader>();
builder.Services.AddSingleton<NtpSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NtpSyncService>());
// 通知出口。交互模式 → 常驻进程直接 ToastNotifier（点击经 -Embedding
// → 命名管道转发回本进程 relay 打开 /time）；服务模式（会话 0）→ ServiceModeNotifier
// 投递到交互会话的 --toast-helper 分支。
if (Environment.UserInteractive)
    builder.Services.AddSingleton<ITimeAnomalyNotifier, ToastNotifier>();
else
    builder.Services.AddSingleton<ITimeAnomalyNotifier, ServiceModeNotifier>();
builder.Services.AddSingleton<TimeAnomalyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TimeAnomalyService>());
builder.Services.AddSingleton<TimeOffsetApplyService>();  // Apply/Restore 端点注入
builder.Services.AddHostedService(sp =>
{
    var settings = sp.GetRequiredService<SettingsService>();
    var anomaly = sp.GetRequiredService<TimeAnomalyService>();
    return new HeartbeatService(sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<WriteQueue>(),
        sp.GetRequiredService<ILogger<HeartbeatService>>(),
        settings, a => anomaly.OnAnomalyDetected(a));
});
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
