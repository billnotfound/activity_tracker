namespace WinActivityTracker.Core.Models;

public static class SystemEventTypes
{
    public const string Sleep = "Sleep";
    public const string Wake = "Wake";
    public const string Shutdown = "Shutdown";
    public const string Start = "Start";
    public const string Idle = "Idle";
}

public static class SystemMarkers
{
    public const string SystemSleepProcess = "__SystemSleep";
    public const string SystemSleepStatus = "SystemSleep";
}
