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

    private string _lastTitle = string.Empty;
    private string _lastArtist = string.Empty;

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

            // Dedup: only write when title or artist actually changed
            if (title == _lastTitle && artist == _lastArtist) return;

            _lastTitle = title;
            _lastArtist = artist;

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
