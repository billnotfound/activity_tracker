namespace WinActivityTracker.Core.Models;

public class SystemEvent
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;  // "Sleep", "Wake", "Shutdown", or "Start"

    // When the event occurred (UTC).
    public DateTime Timestamp { get; set; }

    // For Sleep/Shutdown events: duration until the corresponding Wake/Start.
    // For Wake/Start events: always 0.
    public double DurationSeconds { get; set; }
}
