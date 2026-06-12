namespace WinActivityTracker.Core.Models;

/// <summary>
/// Icon cache row, deduplicated by content hash — same exe icon
/// (e.g. multiple svchost.exe or notepad.exe instances) shares one row.
/// </summary>
public class ProcessIcon
{
    public long Id { get; set; }
    public string IconHash { get; set; } = string.Empty; // SHA256 hex
    public byte[] IconData { get; set; } = [];            // PNG
    public string ColorPrimary { get; set; } = string.Empty;   // #RRGGBB
    public string ColorSecondary { get; set; } = string.Empty; // #RRGGBB
    public string ColorAccent { get; set; } = string.Empty;    // #RRGGBB
}
