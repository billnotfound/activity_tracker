// Records a media playback event from the Windows SystemMediaTransportControls (WinRT API).
// Deduplicated: only INSERTed when title+artist differs from the previous record.
// PlaybackStatus values: "Playing", "Paused", "Stopped", "Closed", "Changing", "Opened".
// Requires WinRT — only works on Windows 10 19041+ with net10.0-windows10.0.19041.0 TFM.
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
