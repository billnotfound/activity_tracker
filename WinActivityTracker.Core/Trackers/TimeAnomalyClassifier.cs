namespace WinActivityTracker.Core.Trackers;

public enum TimeAnomalyKind
{
    Normal, ForwardJump, BackwardJump, SuspiciousHourOffset, Drift, NtpConfirmed
}

public sealed record TimeAnomalyClassification(TimeAnomalyKind Kind, double OffsetSeconds, string? Note);

/// <summary>
/// 纯静态分类器：输入心跳配对增量 (tickDeltaSec, wallDeltaSec) 与可选 NTP 结果。
/// 偏移 = wallDelta − tickDelta。tick 含睡眠时长，所以睡眠时偏移≈0；
/// 只有墙钟被改时偏移才显著偏离 0。
/// </summary>
public static class TimeAnomalyClassifier
{
    public static TimeAnomalyClassification Classify(
        double tickDeltaSec, double wallDeltaSec, TimeReferenceResult? ntp,
        double thresholdSec, double epsilonSec)
    {
        var offset = wallDeltaSec - tickDeltaSec;

        if (offset > thresholdSec)
            return new(TimeAnomalyKind.ForwardJump, offset, null);
        if (offset < -thresholdSec)
            return new(TimeAnomalyKind.BackwardJump, offset, null);

        if (ntp is { Succeeded: true } && Math.Abs(ntp.OffsetSeconds) > epsilonSec)
            return new(TimeAnomalyKind.NtpConfirmed, ntp.OffsetSeconds,
                $"NTP 参照（{ntp.SourceName}）证实时钟偏差 {ntp.OffsetSeconds:F1}s");

        var abs = Math.Abs(offset);
        if (abs > 60)
        {
            var rem = abs % 3600;
            if (rem <= 10 || rem >= 3590)
                return new(TimeAnomalyKind.SuspiciousHourOffset, offset,
                    $"疑似时区问题（偏移 {Math.Round(abs / 3600.0, 1)} 小时）");
            return new(TimeAnomalyKind.Drift, offset, $"时钟漂移 {offset:F1}s");
        }

        return new(TimeAnomalyKind.Normal, offset, null);
    }
}
