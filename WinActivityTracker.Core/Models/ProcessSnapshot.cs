// DEPRECATED — replaced by ProcessSession (session-based process tracking).
// Kept for backward compatibility. No longer written to.
namespace WinActivityTracker.Core.Models;

public class ProcessSnapshot
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
}
