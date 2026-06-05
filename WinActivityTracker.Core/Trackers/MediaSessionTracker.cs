// Tracks media playback via Windows SystemMediaTransportControls (WinRT).
// Session-based: each StartTime/EndTime pair represents one continuous playback period.
// When title, artist, or status changes, the current session is closed and a new one begins.
// EndTime is null while the session is active (currently playing).
// WinRT initialization may fail with InvalidCastException on startup — silently retried.
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

    // Current active session state.
    private long? _currentSessionId;
    private string _currentAppName = string.Empty;
    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private string _currentStatus = string.Empty;

    // Tracks the most recent Wake event handled, so we don't react to the
    // same event on every poll. HeartbeatService writes SystemEvents;
    // this tracker reads them on each cycle.
    private DateTime _lastWakeTime = DateTime.MinValue;

    // Wall-clock time of the last poll iteration. Used for sleep gap detection:
    // if more than 3x the poll interval has passed, the system was asleep and
    // the current session must be closed to avoid counting sleep time as playback.
    private DateTime _lastPollTime = DateTime.UtcNow;

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
                    _lastPollTime = DateTime.UtcNow;
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.MediaPollSeconds), stoppingToken);
                    continue;
                }

                var now = DateTime.UtcNow;
                var gapSec = (now - _lastPollTime).TotalSeconds;
                if (_currentSessionId != null && gapSec > _settings.Settings.MediaPollSeconds * 3)
                {
                    _logger.LogDebug("MediaTracker: sleep gap detected ({Gap}s), closing session", gapSec);
                    using var gapScope = _scopeFactory.CreateScope();
                    var gapDb = gapScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await CloseCurrentSession(gapDb, _lastPollTime);
                }

                await PollMediaSession();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MediaSessionTracker poll error");
            }

            _lastPollTime = DateTime.UtcNow;
            await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.MediaPollSeconds), stoppingToken);
        }

        // Close current session on shutdown.
        try
        {
            using var shutdownScope = _scopeFactory.CreateScope();
            var shutdownDb = shutdownScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CloseCurrentSession(shutdownDb, DateTime.UtcNow);
        }
        catch (Exception ex) { _logger.LogError(ex, "MediaSessionTracker shutdown cleanup error"); }
    }

    private async Task PollMediaSession()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check SystemEvents for a new Wake or Start. If found, close the current
            // session and insert a __SystemSleep marker so the frontend splits playback
            // intervals across sleep/wake boundaries.
            await CheckForWakeEvent(db);

            // Seed current session state from DB on first poll (handles restart).
            if (_currentSessionId == null)
                await SeedCurrentSession(db);

            var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();

            if (session == null)
            {
                await CloseCurrentSession(db, DateTime.UtcNow);
                return;
            }

            var appName = session.SourceAppUserModelId ?? string.Empty;
            if (_settings.Settings.ExcludedProcesses.Contains(appName, StringComparer.OrdinalIgnoreCase))
            {
                await CloseCurrentSession(db, DateTime.UtcNow);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null)
            {
                await CloseCurrentSession(db, DateTime.UtcNow);
                return;
            }

            var title = props.Title ?? string.Empty;
            var artist = props.Artist ?? string.Empty;
            var status = session.GetPlaybackInfo().PlaybackStatus.ToString();

            if (string.IsNullOrEmpty(title))
            {
                await CloseCurrentSession(db, DateTime.UtcNow);
                return;
            }

            // Same session — nothing changed.
            if (_currentSessionId != null
                && _currentAppName == appName
                && _currentTitle == title
                && _currentArtist == artist
                && _currentStatus == status)
                return;

            // Something changed — close current session, start new one.
            await CloseCurrentSession(db, DateTime.UtcNow);
            await StartNewSession(db, appName, title, artist, status, DateTime.UtcNow);

            _logger.LogDebug("Media: {Artist} - {Title} [{Status}]", artist, title, status);
        }
        catch (InvalidCastException)
        {
            // WinRT initialization not ready — silently retry next cycle.
        }
    }

    private async Task CloseCurrentSession(AppDbContext db, DateTime endTime)
    {
        if (_currentSessionId == null) return;

        var s = await db.MediaSessionRecords.FindAsync(_currentSessionId.Value);
        if (s != null) s.EndTime = endTime;
        await db.SaveChangesAsync();

        _currentSessionId = null;
        _currentAppName = string.Empty;
        _currentTitle = string.Empty;
        _currentArtist = string.Empty;
        _currentStatus = string.Empty;
    }

    private async Task StartNewSession(AppDbContext db, string appName, string title, string artist, string status, DateTime startTime)
    {
        var record = new MediaSessionRecord
        {
            StartTime = startTime,
            AppName = appName,
            Title = title,
            Artist = artist,
            PlaybackStatus = status
        };
        db.MediaSessionRecords.Add(record);
        await db.SaveChangesAsync();

        _currentSessionId = record.Id;
        _currentAppName = appName;
        _currentTitle = title;
        _currentArtist = artist;
        _currentStatus = status;
    }

    // On restart, check if there's an open session from the previous run (EndTime IS NULL).
    // Close it at its own StartTime so it's zero-duration — the actual playback was
    // during the previous run, and we can't measure it retroactively.
    private async Task SeedCurrentSession(AppDbContext db)
    {
        var orphan = await db.MediaSessionRecords
            .Where(m => m.EndTime == null)
            .OrderByDescending(m => m.StartTime)
            .FirstOrDefaultAsync();

        if (orphan != null)
        {
            orphan.EndTime = orphan.StartTime;
            await db.SaveChangesAsync();
            _logger.LogDebug("MediaTracker: closed orphan session from previous run (Id={Id})", orphan.Id);
        }
    }

    private async Task CheckForWakeEvent(AppDbContext db)
    {
        var newWake = await db.SystemEvents
            .Where(e => (e.EventType == "Wake" || e.EventType == "Start") && e.Timestamp > _lastWakeTime)
            .OrderBy(e => e.Timestamp)
            .FirstOrDefaultAsync();

        if (newWake != null)
        {
            _logger.LogInformation("MediaTracker: wake/start event at {WakeTime}", newWake.Timestamp);
            _lastWakeTime = newWake.Timestamp;

            // Find the paired off-period event (Sleep or Shutdown) to get the
            // actual time when playback stopped. The Wake event's Timestamp is
            // when the system came back — closing at that time would include the
            // entire sleep duration in the session, inflating listening time.
            var offEvent = await db.SystemEvents
                .Where(e => (e.EventType == "Sleep" || e.EventType == "Shutdown")
                    && e.Timestamp < newWake.Timestamp)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefaultAsync();

            var sessionEndTime = offEvent?.Timestamp ?? newWake.Timestamp;

            // Close current session at the OFF time (sleep/shutdown start),
            // so the sleep period is excluded from playback duration.
            await CloseCurrentSession(db, sessionEndTime);

            // Insert a zero-duration marker so the frontend splits pre/post-sleep playback.
            db.MediaSessionRecords.Add(new MediaSessionRecord
            {
                StartTime = newWake.Timestamp,
                EndTime = newWake.Timestamp,
                AppName = "__SystemSleep",
                Title = "System sleep or program stopped",
                Artist = string.Empty,
                PlaybackStatus = "SystemSleep"
            });
            await db.SaveChangesAsync();
        }
    }
}
