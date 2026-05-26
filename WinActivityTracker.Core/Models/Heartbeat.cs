// Single-row table — tracks the last time the program was alive.
// Updated every 30s by HeartbeatService. On startup, if the gap
// exceeds 33s (30s + 3s tolerance), the system was sleeping or the
// program was stopped — a __SystemSleep marker is inserted.
namespace WinActivityTracker.Core.Models;

public class Heartbeat
{
    public int Id { get; set; } = 1;  // single row
    public DateTime LastTick { get; set; }
}
