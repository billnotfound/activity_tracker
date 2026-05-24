// Periodic snapshot of background processes — those WITHOUT any visible window.
// Taken every ProcessPollSeconds (default 30s). ProcessId is stored for deduplication
// by the /api/processes/snapshot endpoint, which returns only the latest batch.
namespace WinActivityTracker.Core.Models;

public class ProcessSnapshot
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
}
