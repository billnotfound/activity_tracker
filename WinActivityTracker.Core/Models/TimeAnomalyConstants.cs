namespace WinActivityTracker.Core.Models;

public static class TimeAnomalyStatus
{
    public const string Suspicious = "Suspicious";
    public const string Confirmed = "Confirmed";
    public const string Drift = "Drift";
    public const string Pending = "Pending";
    public const string Applied = "Applied";
    public const string Reverted = "Reverted";
    public const string Ignored = "Ignored";
}

public static class TimeAnomalySources
{
    public const string Heartbeat = "Heartbeat";
    public const string EventLog = "EventLog";
    public const string Ntp = "Ntp";
}
