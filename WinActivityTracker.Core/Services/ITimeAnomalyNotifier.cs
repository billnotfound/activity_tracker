using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Services;

/// <summary>确认异常的用户通知出口。Service 项目实现（toast）；测试用 fake。</summary>
public interface ITimeAnomalyNotifier
{
    void NotifyConfirmed(TimeAnomaly anomaly);
    void CancelReminder(long anomalyId);
}
