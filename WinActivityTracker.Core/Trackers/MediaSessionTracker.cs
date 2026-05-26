// Tracks media playback via the Windows SystemMediaTransportControls WinRT API.
//
// This API provides info about the currently-playing media session (e.g. Spotify, foobar2000,
// YouTube in a browser) — title, artist, playback status.
//
// WinRT caveats:
//   - Requires net10.0-windows10.0.19041.0 TFM (WinRT projections)
//   - RequestAsync() can fail with InvalidCastException if WinRT isn't initialized on the thread.
//     We catch and silently retry on the next poll.
//   - Only reports the CURRENT session — if multiple apps are playing, only the active one is seen.
//
// Deduplication: compares title+artist against the previous poll; only INSERTs on change.
// This prevents flooding the DB with identical rows for the same song.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Core.Trackers;

public class MediaSessionTracker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settings;
    private readonly ILogger<MediaSessionTracker> _logger;

    // Per-app dedup: (appName) → (title, artist, status).
    // When switching between media apps (foobar2000 → browser → back),
    // each app's last-known state is preserved independently.
    private readonly Dictionary<string, (string Title, string Artist, string Status)> _perAppState = new();
    private bool _dedupSeeded;

    public MediaSessionTracker(IServiceScopeFactory scopeFactory, SettingsService settings, ILogger<MediaSessionTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MediaSessionTracker started, interval: {Interval}s", _settings.Settings.MediaPollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_settings.Settings.TrackingEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.MediaPollSeconds), stoppingToken);
                    continue;
                }

                await PollMediaSession();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MediaSessionTracker poll error");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.MediaPollSeconds), stoppingToken);
        }
    }

    private async Task PollMediaSession()
    {
        try
        {
            // WinRT async factory — must be called on a thread with a compatible apartment.
            // The first call initializes the WinRT subsystem; subsequent calls are fast.
            var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session == null) return;  // No media playing right now

            var appName = session.SourceAppUserModelId ?? string.Empty;
            if (_settings.Settings.ExcludedProcesses.Contains(appName, StringComparer.OrdinalIgnoreCase))
                return;

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return;

            var title = props.Title ?? string.Empty;
            var artist = props.Artist ?? string.Empty;
            var status = session.GetPlaybackInfo().PlaybackStatus.ToString();

            if (string.IsNullOrEmpty(title)) return;

            // Seed per-app state from DB on first poll (prevents restart duplicates).
            if (!_dedupSeeded)
            {
                _dedupSeeded = true;
                using var seedScope = _scopeFactory.CreateScope();
                var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var groups = seedDb.MediaSessionRecords
                    .GroupBy(m => m.AppName)
                    .Select(g => g.OrderByDescending(m => m.Timestamp).First())
                    .ToList();
                foreach (var r in groups)
                    _perAppState[r.AppName] = (r.Title, r.Artist, r.PlaybackStatus);
            }

            // Per-app dedup: each media source (foobar2000, firefox, etc.) has its
            // own last-known state. Switching between apps doesn't lose dedup info.
            if (_perAppState.TryGetValue(appName, out var prev) &&
                prev.Title == title && prev.Artist == artist && prev.Status == status)
                return;

            _perAppState[appName] = (title, artist, status);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.MediaSessionRecords.Add(new MediaSessionRecord
            {
                Timestamp = DateTime.UtcNow,
                AppName = appName,
                Title = title,
                Artist = artist,
                PlaybackStatus = status
            });

            await db.SaveChangesAsync();

            _logger.LogDebug("Media: {Artist} - {Title} [{Status}]", artist, title, status);
        }
        catch (InvalidCastException)
        {
            // WinRT initialization not ready — silently retry next cycle.
            // This happens occasionally on startup when the COM apartment isn't set up yet.
        }
    }
}
