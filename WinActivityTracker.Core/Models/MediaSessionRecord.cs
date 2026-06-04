// Media playback event from Windows SystemMediaTransportControls (WinRT).
// Deduplicated: only inserted when title+artist differs from the previous record.
// PlaybackStatus values: "Playing", "Paused", "Stopped", "Closed", "Changing", "Opened".
namespace WinActivityTracker.Core.Models;

public class MediaSessionRecord
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string AppName { get; set; } = string.Empty;       // SourceAppUserModelId, e.g. "foobar2000.exe"
    public string Title { get; set; } = string.Empty;         // Song/video title
    public string Artist { get; set; } = string.Empty;        // Artist name (empty for non-music media)
    public string PlaybackStatus { get; set; } = string.Empty;
}
