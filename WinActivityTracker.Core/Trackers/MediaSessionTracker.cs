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

    private bool _hasActiveSession;
    private string _currentAppName = string.Empty;
    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private string _currentStatus = string.Empty;

    private DateTime _lastWakeTime = DateTime.UtcNow;
    private DateTime _lastPollTime = DateTime.UtcNow;

    // Write-time merge: when a session flickers (disappears then reappears),
    // re-open the recently-closed record instead of creating a new one.
    private DateTime _lastCloseTime = DateTime.MinValue;
    private long _lastClosedRecordId;
    private string _lastClosedAppName = string.Empty;
    private string _lastClosedTitle = string.Empty;
    private string _lastClosedArtist = string.Empty;
    private string _lastClosedStatus = string.Empty;

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

        // 启动时把 wake 游标定位到库中最新一条 Wake/Start 事件:历史事件不再重放
        // (旧行为会为每条历史事件插入幽灵 __SystemSleep 标记,并用远古时间戳关闭
        // 当前会话);本会话心跳首跳写入的 Start 事件仍会被正常处理。
        try
        {
            using var seedScope = _scopeFactory.CreateScope();
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            _lastWakeTime = await ResolveInitialWakeCursorAsync(seedDb, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MediaTracker: wake cursor seeding failed, falling back to now");
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_settings.Settings.TrackingEnabled)
                    {
                        if (_hasActiveSession)
                            await CloseCurrentSessionAsync(DateTime.UtcNow);
                        _lastPollTime = DateTime.UtcNow;
                        await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.MediaPollSeconds), stoppingToken);
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    var gapSec = (now - _lastPollTime).TotalSeconds;
                    if (_hasActiveSession && gapSec > _settings.Settings.MediaPollSeconds * 3)
                    {
                        _logger.LogDebug("MediaTracker: sleep gap detected ({Gap}s), closing session", gapSec);
                        using var gapScope = _scopeFactory.CreateScope();
                        var gapDb = gapScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        CloseCurrentSession(gapDb, _lastPollTime);
                        await gapDb.SaveChangesAsync();
                    }

                    await PollMediaSession();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // DB operations update tracked entity state and the in-memory
                    // session state together. If persistence fails, discard the
                    // in-memory side so the next poll re-seeds instead of silently
                    // believing an uncommitted close/start succeeded.
                    ResetInMemorySessionState();
                    _logger.LogError(ex, "MediaSessionTracker poll error");
                }

                _lastPollTime = DateTime.UtcNow;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Settings.MediaPollSeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            try
            {
                using var shutdownScope = _scopeFactory.CreateScope();
                var shutdownDb = shutdownScope.ServiceProvider.GetRequiredService<AppDbContext>();
                CloseCurrentSession(shutdownDb, DateTime.UtcNow);
                await shutdownDb.SaveChangesAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "MediaSessionTracker shutdown cleanup error"); }
        }
    }

    private async Task PollMediaSession()
    {
        _idleDetector.IsMediaPlaying = false;

        try
        {
            await CheckForWakeEvent();

            if (!_hasActiveSession)
                await SeedCurrentSession();

            var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();

            if (session == null)
            {
                if (_hasActiveSession)
                    await CloseCurrentSessionAsync(DateTime.UtcNow);
                return;
            }

            var appName = session.SourceAppUserModelId ?? string.Empty;
            if (_settings.Settings.ExcludedProcesses.Contains(appName, StringComparer.OrdinalIgnoreCase))
            {
                if (_hasActiveSession)
                    await CloseCurrentSessionAsync(DateTime.UtcNow);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null)
            {
                if (_hasActiveSession)
                    await CloseCurrentSessionAsync(DateTime.UtcNow);
                return;
            }

            var title = props.Title ?? string.Empty;
            var artist = props.Artist ?? string.Empty;
            var status = session.GetPlaybackInfo().PlaybackStatus.ToString();

            if (string.IsNullOrEmpty(title))
            {
                if (_hasActiveSession)
                    await CloseCurrentSessionAsync(DateTime.UtcNow);
                return;
            }

            _idleDetector.IsMediaPlaying = status == "Playing";

            if (_hasActiveSession
                && _currentAppName == appName
                && _currentTitle == title
                && _currentArtist == artist
                && _currentStatus == status)
                return;

            var now = DateTime.UtcNow;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            CloseCurrentSession(db, now);
            StartNewSession(db, appName, title, artist, status, now);
            await db.SaveChangesAsync();

            _logger.LogDebug("Media: {Artist} - {Title} [{Status}]", artist, title, status);
        }
        catch (Exception ex) when (ex is InvalidCastException
            or System.Runtime.InteropServices.InvalidComObjectException
            or System.Runtime.InteropServices.COMException
            or System.Runtime.InteropServices.SEHException
            or TypeLoadException
            or FileNotFoundException)
        {
            _logger.LogDebug(ex, "MediaSessionTracker: WinRT not ready, retrying next cycle");
        }
    }

    private async Task CloseCurrentSessionAsync(DateTime endTime)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CloseCurrentSession(db, endTime);
        await db.SaveChangesAsync();
    }

    private void CloseCurrentSession(AppDbContext db, DateTime endTime)
    {
        if (!_hasActiveSession) return;

        // Close only the record that matches the currently tracked session,
        // not all records with EndTime == null (which might be old orphans).
        var session = db.MediaSessionRecords
            .Where(m => m.EndTime == null
                && m.AppName == _currentAppName
                && m.Title == _currentTitle
                && m.Artist == _currentArtist
                && m.PlaybackStatus == _currentStatus)
            .OrderByDescending(m => m.StartTime)
            .FirstOrDefault();

        if (session != null)
        {
            var safeEndTime = NormalizeEndTime(session.StartTime, endTime);
            session.EndTime = safeEndTime;
            _lastCloseTime = safeEndTime;
            _lastClosedRecordId = session.Id;
            _lastClosedAppName = _currentAppName;
            _lastClosedTitle = _currentTitle;
            _lastClosedArtist = _currentArtist;
            _lastClosedStatus = _currentStatus;
        }

        _hasActiveSession = false;
        _currentAppName = string.Empty;
        _currentTitle = string.Empty;
        _currentArtist = string.Empty;
        _currentStatus = string.Empty;
    }

    private void ResetInMemorySessionState()
    {
        _hasActiveSession = false;
        _currentAppName = string.Empty;
        _currentTitle = string.Empty;
        _currentArtist = string.Empty;
        _currentStatus = string.Empty;
        _lastCloseTime = DateTime.MinValue;
        _lastClosedRecordId = 0;
        _lastClosedAppName = string.Empty;
        _lastClosedTitle = string.Empty;
        _lastClosedArtist = string.Empty;
        _lastClosedStatus = string.Empty;
    }

    internal static DateTime NormalizeEndTime(DateTime startTime, DateTime endTime) =>
        endTime < startTime ? startTime : endTime;

    private void StartNewSession(AppDbContext db, string appName, string title, string artist, string status, DateTime startTime)
    {
        var pollSec = _settings.Settings.MediaPollSeconds;
        var gap = startTime - _lastCloseTime;

        // Re-open a recently-closed session if the same song flickered back
        // within the merge window. Don't merge same-batch close+start (gap ~0),
        // which is a real track change, not a flicker.
        if (!_hasActiveSession
            && gap.TotalSeconds >= pollSec * 0.5
            && gap.TotalSeconds <= pollSec * 3
            && _lastClosedAppName == appName
            && _lastClosedTitle == title
            && _lastClosedArtist == artist
            && _lastClosedStatus == status)
        {
            var record = db.MediaSessionRecords.Find(_lastClosedRecordId);
            if (record != null)
            {
                record.EndTime = null;
                record.PlaybackStatus = status;
                _hasActiveSession = true;
                _currentAppName = appName;
                _currentTitle = title;
                _currentArtist = artist;
                _currentStatus = status;
                _logger.LogDebug("Media: re-opened flickered session #{Id} ({Artist} - {Title})",
                    record.Id, artist, title);
                return;
            }
        }

        db.MediaSessionRecords.Add(new MediaSessionRecord
        {
            StartTime = startTime,
            AppName = appName,
            Title = title,
            Artist = artist,
            PlaybackStatus = status
        });

        _hasActiveSession = true;
        _currentAppName = appName;
        _currentTitle = title;
        _currentArtist = artist;
        _currentStatus = status;
    }

    private async Task SeedCurrentSession()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orphans = await db.MediaSessionRecords
            .Where(m => m.EndTime == null)
            .ToListAsync();

        if (orphans.Count > 0)
        {
            foreach (var o in orphans)
                o.EndTime = o.StartTime;
            await db.SaveChangesAsync();
            _logger.LogDebug("MediaTracker: closed {Count} orphan sessions from previous run", orphans.Count);
        }
    }

    /// <summary>
    /// 返回库中最新一条 Wake/Start 事件的时间戳作为 wake 游标起点;无历史事件时
    /// 返回 fallback。用于启动时定位游标,避免重放历史事件。
    /// </summary>
    public static async Task<DateTime> ResolveInitialWakeCursorAsync(AppDbContext db, DateTime fallback)
    {
        var latest = await db.SystemEvents.AsNoTracking()
            .Where(e => e.EventType == SystemEventTypes.Wake || e.EventType == SystemEventTypes.Start)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => (DateTime?)e.Timestamp)
            .FirstOrDefaultAsync();
        return latest ?? fallback;
    }

    private async Task CheckForWakeEvent()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var newWake = await ResolveLatestWakeAfterAsync(db, _lastWakeTime);

        if (newWake == null) return;

        _logger.LogInformation("MediaTracker: wake/start event at {WakeTime}", newWake.Timestamp);

        var offEvent = await db.SystemEvents
            .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown)
                && e.Timestamp < newWake.Timestamp)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();

        var sessionEndTime = offEvent?.Timestamp ?? newWake.Timestamp;
        CloseCurrentSession(db, sessionEndTime);

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
        // Advance only after the marker/session close committed. A failed save
        // must retry this wake rather than permanently skipping it.
        _lastWakeTime = newWake.Timestamp;
    }

    internal static Task<SystemEvent?> ResolveLatestWakeAfterAsync(
        AppDbContext db, DateTime cursor) => db.SystemEvents
            .Where(e => (e.EventType == SystemEventTypes.Wake || e.EventType == SystemEventTypes.Start)
                && e.Timestamp > cursor)
            // Consume a burst at once. Processing the oldest row one poll at a
            // time repeatedly closed/reopened the same paused media session.
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();
}
