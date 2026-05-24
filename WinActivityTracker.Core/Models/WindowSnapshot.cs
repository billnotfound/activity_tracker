// Point-in-time snapshot of all visible top-level windows, taken every WindowPollSeconds.
// Unlike FocusChange which tracks transitions, this table records what was open.
// IsFocused identifies which single window had keyboard focus at snapshot time.
// This table grows fast (one row per visible window per poll) — use /api/db/cleanup regularly.
namespace WinActivityTracker.Core.Models;

public class WindowSnapshot
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public bool IsFocused { get; set; }
}
