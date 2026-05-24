// Records a single focus session: user switched TO this window and stayed for DurationSeconds.
// Timestamp is the START time of the focus period (when the user switched to this window).
// A new FocusChange is INSERTed only when focus leaves — duration is calculated retroactively.
// Query example: SUM(DurationSeconds) GROUP BY ProcessName gives total daily focus time per app.
namespace WinActivityTracker.Core.Models;

public class FocusChange
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;

    // How long the user stayed on this window before switching away or going idle.
    // Values under 0.5s are discarded as noise (accidental alt-tab flickers).
    public double DurationSeconds { get; set; }
}
