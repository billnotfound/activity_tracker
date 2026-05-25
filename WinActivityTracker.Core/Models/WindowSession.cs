// Window open/close session — replaces the old WindowSnapshot (point-in-time) table.
// OpenTime = when the window first appeared. CloseTime = when it disappeared (nullable until closed).
// Data volume: ~100-500 rows/day vs ~100K+ for WindowSnapshots.
namespace WinActivityTracker.Core.Models;

public class WindowSession
{
    public long Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public DateTime OpenTime { get; set; }
    public DateTime? CloseTime { get; set; }
}
