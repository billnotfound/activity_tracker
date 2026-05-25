// Process start/stop session — replaces the old ProcessSnapshot (point-in-time) table.
// StartTime = when the process first appeared. EndTime = when it disappeared (nullable).
namespace WinActivityTracker.Core.Models;

public class ProcessSession
{
    public long Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}
