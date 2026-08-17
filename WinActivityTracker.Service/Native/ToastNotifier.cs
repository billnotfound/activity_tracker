// Task 10: 确认时间异常的系统 Toast 通知（普通交互模式）。
// 基于 CommunityToolkit.Labs.Notifications（免打包、无 AOT/XAML 依赖；实测结论见
// cache/toast-spike/RESULT.md）：
//   - 立即弹一条 + 2 分钟后第二条（排程句柄持有对象引用以便取消——Labs 的 Id setter
//     在无交互会话下抛 0x803E0120，RemoveFromSchedule 按对象引用取消）
//   - 激活：launch 参数内嵌 action=open-time → 系统以 -Embedding 拉起新实例 →
//     ToastActivationClient 命名管道转发 → 常驻实例的 relay 打开 /time 面板
// 所有 Labs 调用都 try/catch：通知失败不影响追踪主流程。
using CommunityToolkit.Notifications;
using Windows.UI.Notifications;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

/// <summary>Windows toast 通知：确认异常立即弹一条 + 2 分钟后第二条，共 2 次。</summary>
public sealed class ToastNotifier : ITimeAnomalyNotifier
{
    private readonly Dictionary<long, ScheduledToastNotification> _scheduled = new();
    private readonly object _lock = new();

    public void NotifyConfirmed(TimeAnomaly anomaly)
    {
        try
        {
            var title = I18nService._("timeAnomaly.toastTitle");
            var body = I18nService._("timeAnomaly.toastBody", anomaly.OffsetSeconds.ToString("F0"));
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddArgument("action", "open-time")
                .AddArgument("anomalyId", anomaly.Id);

            ShowNow(builder);
            var reminder = ScheduleReminder(builder, anomaly.Id);
            if (reminder != null)
            {
                lock (_lock) _scheduled[anomaly.Id] = reminder;
            }
        }
        catch { /* 通知失败静默：追踪主流程不得受影响 */ }
    }

    public void CancelReminder(long anomalyId)
    {
        ScheduledToastNotification? reminder = null;
        lock (_lock)
        {
            if (_scheduled.Remove(anomalyId, out var r)) reminder = r;
        }
        if (reminder != null)
        {
            try { ToastNotificationManagerCompat.CreateToastNotifier().RemoveFromSchedule(reminder); }
            catch { }
        }
    }

    private static void ShowNow(ToastContentBuilder builder)
    {
        try { builder.Show(); }
        catch { /* 无交互会话/无 toast 基础设施时静默（Ruling B3 预期） */ }
    }

    private static ScheduledToastNotification? ScheduleReminder(ToastContentBuilder builder, long anomalyId)
    {
        try
        {
            var scheduled = new ScheduledToastNotification(
                builder.GetToastContent().GetXml(), DateTimeOffset.Now.AddMinutes(2));
            ToastNotificationManagerCompat.CreateToastNotifier().AddToSchedule(scheduled);
            return scheduled;
        }
        catch { return null; }
    }
}
