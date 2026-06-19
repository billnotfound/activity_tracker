// On-demand Kestrel + Vue SPA server. Only runs when the user opens the dashboard.
// Stops after idle timeout (default 3 min). PortWatcher rebinds the port when Kestrel
// is down so a frozen-tab refresh always gets a loading page, never "connection refused".
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Service.Api;

namespace WinActivityTracker.Service;

public class DashboardServer
{
    private readonly IServiceProvider _services;
    private readonly string _dbPath;
    private readonly PortWatcher _portWatcher;
    private readonly int _port;

    private WebApplication? _app;
    private DateTime _lastRequestTime;
    private CancellationTokenSource? _idleCts;
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public int Port => _port;

    public DashboardServer(IServiceProvider services, string dbPath, int port, PortWatcher portWatcher)
    {
        _services = services;
        _dbPath = dbPath;
        _port = port;
        _portWatcher = portWatcher;
    }

    public async Task StartAsync()
    {
        lock (_lock)
        {
            if (_app != null || IsRunning) return;
        }

        _portWatcher.Stop();

        var builder = WebApplication.CreateSlimBuilder([]);

        // Forward singleton services from the Host so API endpoints share the
        // same instances (SettingsService, TagService, etc.).
        builder.Services.AddSingleton(_services.GetRequiredService<AppPaths>());
        builder.Services.AddSingleton(_services.GetRequiredService<I18nService>());
        builder.Services.AddSingleton(_services.GetRequiredService<SettingsService>());
        builder.Services.AddSingleton(_services.GetRequiredService<TagService>());
        builder.Services.AddSingleton(_services.GetRequiredService<TitleNormalizer>());
        builder.Services.AddSingleton(_services.GetRequiredService<ProcessNameCache>());
        builder.Services.AddSingleton(_services.GetRequiredService<IconService>());

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNameCaseInsensitive = true;
            o.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Limits.MaxConcurrentConnections = 10;
            o.AddServerHeader = false;
        });

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared"));

        builder.Services.AddCors(c => c.AddDefaultPolicy(p =>
            p.WithOrigins($"http://localhost:{_port}", "http://localhost:5000")
             .AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseCors();

        // SPA static files
        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var serveSpa = Directory.Exists(webRoot);
        Microsoft.Extensions.FileProviders.PhysicalFileProvider? fp = null;
        if (serveSpa)
        {
            fp = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fp });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fp });
        }

        // API endpoints
        app.MapAdminEndpoints();
        app.MapFocusEndpoints();
        app.MapMediaEndpoints();
        app.MapWindowEndpoints();
        app.MapTagEndpoints();
        app.MapIconEndpoints();

        if (serveSpa && fp != null)
            app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fp });

        // Idle tracking middleware — records the last request time for the idle monitor.
        _lastRequestTime = DateTime.UtcNow;
        app.Use(async (context, next) =>
        {
            _lastRequestTime = DateTime.UtcNow;
            await next();
        });

        app.Urls.Add($"http://localhost:{_port}");

        await app.StartAsync();

        lock (_lock)
        {
            _app = app;
            IsRunning = true;
        }

        // Start idle timeout monitor in background.
        _idleCts = new CancellationTokenSource();
        _ = MonitorIdle(_idleCts.Token);
    }

    public async Task StopAsync()
    {
        WebApplication? app;
        lock (_lock)
        {
            if (_app == null) return;
            app = _app;
            _app = null;
            IsRunning = false;
        }

        _idleCts?.Cancel();
        _idleCts = null;

        try { await app.StopAsync(); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        try { await app.DisposeAsync(); }
        catch { }

        _portWatcher.Start();
    }

    private async Task MonitorIdle(CancellationToken ct)
    {
        var settings = _services.GetRequiredService<SettingsService>();
        var timeout = Math.Max(1, settings.Settings.WebIdleTimeoutMinutes);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            if (ct.IsCancellationRequested) break;

            var idle = DateTime.UtcNow - _lastRequestTime;
            if (idle.TotalMinutes >= timeout)
            {
                await StopAsync();
                break;
            }
        }
    }
}
