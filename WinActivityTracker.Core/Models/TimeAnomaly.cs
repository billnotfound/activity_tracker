// 一次时间异常检测结论 + 偏移段。段范围 (FromWall, ToWall) 用"存储的墙钟"
// 坐标定义；OffsetSeconds = 段内"存储墙钟 − 真实时间"（+前调 / −回调）。
// Status 见 TimeAnomalyStatus。Suspicious/Drift 无段范围，纯静默记录。
namespace WinActivityTracker.Core.Models;

public class TimeAnomaly
{
    public long Id { get; set; }
    public DateTime DetectedAt { get; set; }
    public string Source { get; set; } = string.Empty;  // TimeAnomalySources
    public DateTime? OldTime { get; set; }   // 事件日志来源的精确旧时间
    public DateTime? NewTime { get; set; }   // 事件日志来源的精确新时间
    public double OffsetSeconds { get; set; } // +前调 / −回调
    public string Status { get; set; } = string.Empty;  // TimeAnomalyStatus
    public DateTime? FromWall { get; set; }
    public DateTime? ToWall { get; set; }
    public string? Note { get; set; }
    public DateTime? LastAppliedAt { get; set; }
}
