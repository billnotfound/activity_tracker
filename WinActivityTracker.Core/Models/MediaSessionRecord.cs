// Media playback session from Windows SystemMediaTransportControls (WinRT).
// Session-based: StartTime marks when playback began, EndTime when it stopped/changed.
// EndTime is null while the session is active (currently playing).
// PlaybackStatus values: "Playing", "Paused", "Stopped", "Closed", "Changing", "Opened".
namespace WinActivityTracker.Core.Models;

public class MediaSessionRecord
{
    public long Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string AppName { get; set; } = string.Empty;       // SourceAppUserModelId, e.g. "foobar2000.exe"
    public string Title { get; set; } = string.Empty;         // Song/video title
    public string Artist { get; set; } = string.Empty;        // Artist name (empty for non-music media)
    public string PlaybackStatus { get; set; } = string.Empty;
}
