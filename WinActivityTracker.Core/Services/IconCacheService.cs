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

        // Load existing icons into cache on startup
        await LoadExistingIconsToCache();

        // Poll for new processes every 10 seconds
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForNewProcesses(stoppingToken);
                await Task.Delay(10000, stoppingToken); // Check every 10 seconds
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for new processes");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("IconCacheService stopped");
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

    private async Task CheckForNewProcesses(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Get distinct process names from recent focus changes (last hour)
        var recentProcesses = await db.FocusChanges
            .Where(fc => fc.Timestamp >= DateTime.UtcNow.AddHours(-1))
            .Select(fc => fc.ProcessName)
            .Distinct()
            .ToListAsync(stoppingToken);

        foreach (var processName in recentProcesses)
        {
            // Check if icon is already in cache
            if (_extractedIcons.ContainsKey(processName))
            {
                continue; // Already extracted
            }

            // Add to extraction queue
            _extractionQueue.Enqueue(processName);
            _logger.LogInformation("Found new process without icon: {ProcessName}", processName);
        }

        // Process extraction queue
        await ProcessExtractionQueue(stoppingToken);
    }

    private async Task ProcessExtractionQueue(CancellationToken stoppingToken)
    {
        while (_extractionQueue.TryDequeue(out var processName))
        {
            try
            {
                // Double-check cache (in case another thread extracted it)
                if (_extractedIcons.ContainsKey(processName))
                {
                    continue;
                }

                await _extractionSemaphore.WaitAsync(stoppingToken);
                try
                {
                    // Triple-check inside semaphore
                    if (_extractedIcons.ContainsKey(processName))
                    {
                        continue;
                    }

                    _logger.LogInformation("Extracting icon for new process: {ProcessName}", processName);

                    // Extract icon
                    var success = await _iconService.ExtractAndSaveIconAsync(processName);

                    if (success)
                    {
                        // Add to cache
                        _extractedIcons.TryAdd(processName, true);
                        _logger.LogInformation("Successfully extracted icon for {ProcessName}", processName);
                    }
                    else
                    {
                        // Mark as attempted (don't retry immediately)
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
