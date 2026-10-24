// On-demand Kestrel + Vue SPA server, runs only while the dashboard is open.
// Stops after idle timeout (default 3 min). PortWatcher rebinds the port when
// Kestrel is down so a frozen-tab refresh gets a loading page, not "connection refused".
using System.Net;
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
    private Microsoft.Extensions.FileProviders.PhysicalFileProvider? _fileProvider;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);

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
        // 门 + 门内检查:并发调用(托盘双击/toast relay/PortWatcher 回拔)中只有一个
        // 执行 Kestrel 绑定,其余直接返回——避免两个调用都通过旧检查后败者绑端口抛出
        // AddressInUseException(async void 处理器上崩溃整个进程)。
        await _startGate.WaitAsync();
        WebApplication? startingApp = null;
        Microsoft.Extensions.FileProviders.PhysicalFileProvider? startingFiles = null;
        ConfigurationManager? startingConfiguration = null;
        try
        {
            lock (_lock)
            {
                if (_app != null || IsRunning) return;
            }

            _portWatcher.Stop();

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
                ContentRootPath = AppContext.BaseDirectory
            });
            startingConfiguration = builder.Configuration;

            // Forward Host singletons so API endpoints share the same instances.
            builder.Services.AddSingleton(_services.GetRequiredService<AppPaths>());
            builder.Services.AddSingleton(_services.GetRequiredService<I18nService>());
            builder.Services.AddSingleton(_services.GetRequiredService<SettingsService>());
            builder.Services.AddSingleton(_services.GetRequiredService<TagService>());
            builder.Services.AddSingleton(_services.GetRequiredService<TitleNormalizer>());
            builder.Services.AddSingleton(_services.GetRequiredService<ProcessNameCache>());
            builder.Services.AddSingleton(_services.GetRequiredService<IconService>());
            builder.Services.AddSingleton(_services.GetRequiredService<WriteQueue>());
            builder.Services.AddSingleton(_services.GetRequiredService<SystemPressure>());
            // Forward time-anomaly services so endpoints share the Host instances.
            builder.Services.AddSingleton(_services.GetRequiredService<TimeAnomalyService>());
            builder.Services.AddSingleton(_services.GetRequiredService<NtpSyncService>());
            builder.Services.AddSingleton(_services.GetRequiredService<TimeOffsetApplyService>());

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
                options.UseSqlite(SqliteConnectionStrings.Build(_dbPath)));

            builder.Services.AddCors(c => c.AddDefaultPolicy(p =>
                p.WithOrigins($"http://localhost:{_port}", "http://localhost:5000")
                 .AllowAnyMethod().AllowAnyHeader()));

            var app = startingApp = builder.Build();
            app.Use(async (context, next) =>
            {
                _lastRequestTime = DateTime.UtcNow;
                if (!IsAllowedHost(context.Request.Host))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (IsUnsafeMethod(context.Request.Method)
                    && context.Request.Headers.TryGetValue("Origin", out var origins)
                    && !IsAllowedOrigin(origins.ToString()))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                await next();
            });
            app.UseCors();

            // SPA static files
            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var serveSpa = Directory.Exists(webRoot);
            Microsoft.Extensions.FileProviders.PhysicalFileProvider? fp = null;
            if (serveSpa)
            {
                fp = startingFiles = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot);
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
            app.MapTimeAnomalyEndpoints();

            if (serveSpa && fp != null)
                app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fp });

            // The middleware above also sees static-file and SPA fallback requests.
            _lastRequestTime = DateTime.UtcNow;

            app.Urls.Add($"http://localhost:{_port}");

            await app.StartAsync();

            lock (_lock)
            {
                _app = app;
                _fileProvider = fp;
                IsRunning = true;
            }

            // Start idle timeout monitor in background.
            _idleCts = new CancellationTokenSource();
            _ = MonitorIdle(_idleCts.Token);
        }
        catch
        {
            // Failed starts never reach _app, so StopAsync cannot clean them up.
            try
            {
                if (startingApp != null) await startingApp.DisposeAsync();
                else (startingConfiguration as IDisposable)?.Dispose();
            }
            finally
            {
                startingFiles?.Dispose();
                try { _portWatcher.Start(); }
                catch { /* The port may still be held by another application. */ }
            }
            throw;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private bool IsAllowedHost(HostString host)
    {
        if (!host.HasValue || (host.Port.HasValue && host.Port.Value != _port))
            return false;
        return IsLoopbackName(host.Host);
    }

    private bool IsAllowedOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !IsLoopbackName(uri.Host))
            return false;

        return uri.Port == _port || uri.Port == 5000;
    }

    private static bool IsLoopbackName(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static bool IsUnsafeMethod(string method)
        => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);

    public Task StopAsync() => StopAsync(CancellationToken.None);

    private async Task StopAsync(CancellationToken monitorToken)
    {
        // Stop and start share the gate: no replacement host can bind while the
        // previous host is still shutting down or its port watcher is restarting.
        await _startGate.WaitAsync();
        try
        {
            if (monitorToken.IsCancellationRequested) return;
            WebApplication? app;
            Microsoft.Extensions.FileProviders.PhysicalFileProvider? fp;
            lock (_lock)
            {
                if (_app == null) return;
                app = _app;
                fp = _fileProvider;
                _app = null;
                _fileProvider = null;
                IsRunning = false;
            }

            var idleCts = _idleCts;
            _idleCts = null;
            idleCts?.Cancel();

            try { await app.StopAsync(); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                try { await app.DisposeAsync(); }
                finally
                {
                    // StaticFileOptions does not transfer ownership to the host.
                    fp?.Dispose();
                    idleCts?.Dispose();
                }
            }

            _portWatcher.Start();
        }
        finally { _startGate.Release(); }
    }

    private async Task MonitorIdle(CancellationToken ct)
    {
        var settings = _services.GetRequiredService<SettingsService>();
        var timeout = Math.Max(1, settings.Settings.WebIdleTimeoutMinutes);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                var idle = DateTime.UtcNow - _lastRequestTime;
                if (idle.TotalMinutes >= timeout)
                {
                    await StopAsync(ct);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
