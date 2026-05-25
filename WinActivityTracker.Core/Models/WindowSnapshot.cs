// DEPRECATED — replaced by WindowSession (session-based window tracking).
// Kept for backward compatibility with existing databases. No longer written to.
namespace WinActivityTracker.Core.Models;

public class WindowSnapshot
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public bool IsFocused { get; set; }
}
