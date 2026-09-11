// 一次"应用偏移"的精确改动记录：RowsJson 按表列出受影响行主键 Id 区间，
// ShiftSeconds = 相对原始数据的平移量（原始→当前: ts += ShiftSeconds；
// 恢复 = ts -= ShiftSeconds）。v2 使用连续区间压缩；恢复仍兼容旧版 Id 数组。
// 这是恢复原始数据的唯一凭据（非全量备份）。
namespace WinActivityTracker.Core.Models;

public class TimeOffsetApplication
{
    public long Id { get; set; }
    public long AnomalyId { get; set; }
    public DateTime AppliedAt { get; set; }
    public double ShiftSeconds { get; set; }
    public string RowsJson { get; set; } = string.Empty;
    public DateTime MaxRowTimestamp { get; set; }  // 引用数据行的最大时间戳（平移后，post-shift），供清理协同
    public DateTime? RevertedAt { get; set; }
}
