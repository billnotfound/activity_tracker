namespace WinActivityTracker.Core.Models;

public class SystemEvent
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;  // "Sleep" / "Wake"

    // When the event occurred (UTC).
    public DateTime Timestamp { get; set; }

    // For Sleep events: how long the sleep lasted (seconds until the corresponding Wake).
    // For Wake events: always 0.
    public double DurationSeconds { get; set; }
}
