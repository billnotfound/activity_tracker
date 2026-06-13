namespace WinActivityTracker.Core.Models;

/// <summary>
/// Maps process name to icon hash, allowing icon lookup for processes that aren't currently running.
/// Multiple process names (e.g., "Code.exe" from different paths) can share the same icon hash.
/// </summary>
public class ProcessIconMapping
{
    public long Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;  // e.g., "Code.exe"
    public string ExePath { get; set; } = string.Empty;       // Full path for reference
    public string IconHash { get; set; } = string.Empty;      // FK to ProcessIcon.IconHash
    public DateTime LastSeen { get; set; }                    // When this mapping was last updated
}
