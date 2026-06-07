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
    private readonly IdleDetector _idleDetector;
    private readonly ILogger<MediaSessionTracker> _logger;

    private long? _currentSessionId;
    private string _currentAppName = string.Empty;
    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private string _currentStatus = string.Empty;

    private DateTime _lastWakeTime = DateTime.MinValue;
    private DateTime _lastPollTime = DateTime.UtcNow;

    public MediaSessionTracker(IServiceScopeFactory scopeFactory, SettingsService settings,
        IdleDetector idleDetector, ILogger<MediaSessionTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _idleDetector = idleDetector;
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
        _idleDetector.IsMediaPlaying = false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await CheckForWakeEvent(db);

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

            _idleDetector.IsMediaPlaying = status == "Playing";

            if (_currentSessionId != null
                && _currentAppName == appName
                && _currentTitle == title
                && _currentArtist == artist
                && _currentStatus == status)
                return;

            await CloseCurrentSession(db, DateTime.UtcNow);
            await StartNewSession(db, appName, title, artist, status, DateTime.UtcNow);

            _logger.LogDebug("Media: {Artist} - {Title} [{Status}]", artist, title, status);
        }
        catch (InvalidCastException)
        {
            _logger.LogDebug("MediaSessionTracker: WinRT not ready, retrying next cycle");
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
            .Where(e => (e.EventType == SystemEventTypes.Wake || e.EventType == SystemEventTypes.Start)
                && e.Timestamp > _lastWakeTime)
            .OrderBy(e => e.Timestamp)
            .FirstOrDefaultAsync();

        if (newWake == null) return;

        _logger.LogInformation("MediaTracker: wake/start event at {WakeTime}", newWake.Timestamp);
        _lastWakeTime = newWake.Timestamp;

        var offEvent = await db.SystemEvents
            .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown)
                && e.Timestamp < newWake.Timestamp)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();

        var sessionEndTime = offEvent?.Timestamp ?? newWake.Timestamp;
        await CloseCurrentSession(db, sessionEndTime);

        db.MediaSessionRecords.Add(new MediaSessionRecord
        {
            StartTime = newWake.Timestamp,
            EndTime = newWake.Timestamp,
            AppName = SystemMarkers.SystemSleepProcess,
            Title = "System sleep or program stopped",
            Artist = string.Empty,
            PlaybackStatus = SystemMarkers.SystemSleepStatus
        });
        await db.SaveChangesAsync();
    }
}
