// Icon cache service — tracks which processes have icons extracted
// and triggers extraction for new processes automatically.
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using WinActivityTracker.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace WinActivityTracker.Core.Services;

public class IconCacheService : BackgroundService
{
    private readonly ILogger<IconCacheService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IconService _iconService;
    private readonly SystemPressure _systemPressure;

    // In-memory cache of process names that have icons extracted
    private readonly ConcurrentDictionary<string, bool> _extractedIcons = new(StringComparer.OrdinalIgnoreCase);

    // Queue for pending icon extraction
    private readonly Channel<string> _extractionQueue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);

    public IconCacheService(
        ILogger<IconCacheService> logger,
        IServiceScopeFactory scopeFactory,
        IconService iconService,
        SystemPressure systemPressure)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _iconService = iconService;
        _systemPressure = systemPressure;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IconCacheService started");

        await LoadExistingIconsToCache();
        var lastRecheck = TimeProvider.System.GetTimestamp();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_systemPressure.GetCurrent() != PressureLevel.Normal)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                await ProcessExtractionQueue(stoppingToken);
                var remaining = TimeSpan.FromHours(1) - TimeProvider.System.GetElapsedTime(lastRecheck);
                if (remaining <= TimeSpan.Zero)
                {
                    await RecheckAllKnownProcesses(stoppingToken);
                    lastRecheck = TimeProvider.System.GetTimestamp();
                    continue;
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(remaining);
                try
                {
                    if (!await _extractionQueue.Reader.WaitToReadAsync(wait.Token)) break;
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IconCacheService error");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }

        _logger.LogInformation("IconCacheService stopped");
    }

    /// <summary>
    /// Called by trackers when a new process gains focus. If the process
    /// isn't in the cache, queues it for icon extraction. Returns immediately
    /// — extraction happens on a background task.
    /// </summary>
    public void NotifyProcess(string processName)
    {
        if (_extractedIcons.ContainsKey(processName)) return;
        if (!_pending.TryAdd(processName, 0)) return;
        if (!_extractionQueue.Writer.TryWrite(processName))
        {
            _pending.TryRemove(processName, out _);
            return;
        }
        _logger.LogDebug("IconCacheService: queued {Process} for icon extraction", processName);
    }

    private async Task LoadExistingIconsToCache()
    {
        try
        {
            var existingIcons = await _iconService.GetAllProcessNamesWithIconsAsync();
            foreach (var processName in existingIcons)
            {
                _extractedIcons.TryAdd(processName, true);
            }
            _logger.LogInformation("Loaded {Count} existing icons into cache", _extractedIcons.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load existing icons into cache");
        }
    }

    /// <summary>
    /// Hourly batch re-check of all known processes for icon changes.
    /// Calls ExtractAndSaveIconAsync which triggers hash comparison in
    /// EnsureIconAsync — creates versioned mapping if icon changed.
    /// </summary>
    internal async Task RecheckAllKnownProcesses(CancellationToken cancellationToken = default)
    {
        if (_systemPressure.GetCurrent() != PressureLevel.Normal)
        {
            _logger.LogDebug("Skipping hourly icon re-check (system pressure)");
            return;
        }

        _logger.LogDebug("Hourly icon re-check for {Count} processes", _extractedIcons.Count);
        foreach (var (processName, hasIcon) in _extractedIcons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var success = await _iconService.ExtractAndSaveIconAsync(processName, forceRefresh: true);
                // A stopped process does not invalidate an existing stored icon.
                if (success || !hasIcon) _extractedIcons[processName] = success;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Re-check failed for {Process}", processName);
            }
        }
    }

    private async Task ProcessExtractionQueue(CancellationToken cancellationToken)
    {
        if (_systemPressure.GetCurrent() != PressureLevel.Normal)
        {
            _logger.LogDebug("Deferring icon extraction queue (system pressure)");
            return;
        }

        while (_extractionQueue.Reader.TryRead(out var processName))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_extractedIcons.ContainsKey(processName))
                    continue;

                _logger.LogInformation("Extracting icon for new process: {ProcessName}", processName);

                var success = await _iconService.ExtractAndSaveIconAsync(processName);

                if (success)
                {
                    _extractedIcons.TryAdd(processName, true);
                    _logger.LogInformation("Successfully extracted icon for {ProcessName}", processName);
                }
                else
                {
                    _extractedIcons.TryAdd(processName, false);
                    _logger.LogWarning("Failed to extract icon for {ProcessName}", processName);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing icon extraction for {ProcessName}", processName);
            }
            finally { _pending.TryRemove(processName, out _); }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _extractionQueue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    public async Task RefreshCacheAsync()
    {
        _extractedIcons.Clear();
        await LoadExistingIconsToCache();
    }

    public bool HasIcon(string processName)
    {
        return _extractedIcons.TryGetValue(processName, out var hasIcon) && hasIcon;
    }

    public (int Total, int WithIcons, int Failed) GetCacheStats()
    {
        var total = _extractedIcons.Count;
        var withIcons = _extractedIcons.Count(kvp => kvp.Value);
        var failed = _extractedIcons.Count(kvp => !kvp.Value);
        return (total, withIcons, failed);
    }
}
