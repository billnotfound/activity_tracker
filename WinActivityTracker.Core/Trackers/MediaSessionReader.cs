using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace WinActivityTracker.Core.Trackers;

internal sealed record MediaSessionSnapshot(string AppName, string Title, string Artist, string Status);

// Unavailable is distinct from a successful read with no session: a temporary
// shell failure must not look like the player closed.
internal readonly record struct MediaSessionReadResult(bool IsAvailable, MediaSessionSnapshot? Session);

internal interface IMediaSessionConnection
{
    Task<MediaSessionSnapshot?> ReadAsync(IReadOnlyCollection<string> excludedProcesses);
}

// Owned by the tracker's single polling loop; no event callbacks mutate it.
internal sealed class MediaSessionReader
{
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly Func<Task<IMediaSessionConnection>> _connect;
    private readonly TimeProvider _timeProvider;
    private IMediaSessionConnection? _connection;
    private long? _failedAt;

    public MediaSessionReader() : this(WinRtMediaSessionConnection.ConnectAsync, TimeProvider.System) { }

    internal MediaSessionReader(Func<Task<IMediaSessionConnection>> connect, TimeProvider timeProvider)
    {
        _connect = connect;
        _timeProvider = timeProvider;
    }

    public async Task<MediaSessionReadResult> ReadAsync(IReadOnlyCollection<string> excludedProcesses)
    {
        if (_failedAt is long failedAt && _timeProvider.GetElapsedTime(failedAt) < RetryDelay)
            return default;

        try
        {
            // RequestAsync creates a shell/COM connection, not a playback snapshot.
            // Keep it across polls, including periods with no current session.
            _connection ??= await _connect();
            var session = await _connection.ReadAsync(excludedProcesses);
            _failedAt = null;
            return new MediaSessionReadResult(true, session);
        }
        catch (Exception ex) when (IsWinRtUnavailable(ex))
        {
            _connection = null;
            _failedAt = _timeProvider.GetTimestamp();
            throw;
        }
    }

    public void Reset()
    {
        _connection = null;
        _failedAt = null;
    }

    internal static bool IsWinRtUnavailable(Exception ex) => ex is InvalidCastException
        or InvalidComObjectException or COMException or SEHException
        or TypeLoadException or FileNotFoundException;
}

internal sealed class WinRtMediaSessionConnection(
    GlobalSystemMediaTransportControlsSessionManager manager) : IMediaSessionConnection
{
    public static async Task<IMediaSessionConnection> ConnectAsync() =>
        new WinRtMediaSessionConnection(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

    public async Task<MediaSessionSnapshot?> ReadAsync(IReadOnlyCollection<string> excludedProcesses)
    {
        var session = manager.GetCurrentSession();
        if (session == null) return null;

        var appName = session.SourceAppUserModelId ?? string.Empty;
        if (excludedProcesses.Contains(appName, StringComparer.OrdinalIgnoreCase))
            return null;

        var props = await session.TryGetMediaPropertiesAsync();
        if (props == null || string.IsNullOrEmpty(props.Title)) return null;

        return new MediaSessionSnapshot(appName, props.Title, props.Artist ?? string.Empty,
            session.GetPlaybackInfo().PlaybackStatus.ToString());
    }
}
