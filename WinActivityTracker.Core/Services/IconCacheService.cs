// Icon cache service — tracks which processes have icons extracted
// and triggers extraction for new processes automatically.
using System.Collections.Concurrent;
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

    // In-memory cache of process names that have icons extracted
    private readonly ConcurrentDictionary<string, bool> _extractedIcons = new();

    // Queue for pending icon extraction
    private readonly ConcurrentQueue<string> _extractionQueue = new();

    // Semaphore to prevent concurrent extractions
    private readonly SemaphoreSlim _extractionSemaphore = new(1, 1);

    public IconCacheService(
        ILogger<IconCacheService> logger,
        IServiceScopeFactory scopeFactory,
        IconService iconService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _iconService = iconService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IconCacheService started");

        await LoadExistingIconsToCache();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain any queued extractions, then wait for hourly re-check
                await ProcessExtractionQueue();
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                await RecheckAllKnownProcesses();
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IconCacheService error");
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
        _extractionQueue.Enqueue(processName);
        _logger.LogDebug("IconCacheService: queued {Process} for icon extraction", processName);
        _ = Task.Run(async () =>
        {
            try { await ProcessExtractionQueue(); }
            catch (Exception ex) { _logger.LogError(ex, "NotifyProcess extraction error"); }
        });
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
    private async Task RecheckAllKnownProcesses()
    {
        _logger.LogDebug("Hourly icon re-check for {Count} processes", _extractedIcons.Count);
        foreach (var (processName, hasIcon) in _extractedIcons)
        {
            if (!hasIcon) continue;
            try
            {
                await _iconService.ExtractAndSaveIconAsync(processName);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Re-check failed for {Process}", processName);
            }
        }
    }

    private async Task ProcessExtractionQueue()
    {
        while (_extractionQueue.TryDequeue(out var processName))
        {
            try
            {
                if (_extractedIcons.ContainsKey(processName))
                    continue;

                await _extractionSemaphore.WaitAsync();
                try
                {
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
                finally
                {
                    _extractionSemaphore.Release();
                }
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                _logger.LogError(ex, "Error processing icon extraction for {ProcessName}", processName);
            }
        }
    }

    // Manual refresh cache (can be called from API)
    public async Task RefreshCacheAsync()
    {
        _extractedIcons.Clear();
        await LoadExistingIconsToCache();
    }

    // Check if a process has an icon
    public bool HasIcon(string processName)
    {
        return _extractedIcons.TryGetValue(processName, out var hasIcon) && hasIcon;
    }

    // Get cache statistics
    public (int Total, int WithIcons, int Failed) GetCacheStats()
    {
        var total = _extractedIcons.Count;
        var withIcons = _extractedIcons.Count(kvp => kvp.Value);
        var failed = _extractedIcons.Count(kvp => !kvp.Value);
        return (total, withIcons, failed);
    }
}
