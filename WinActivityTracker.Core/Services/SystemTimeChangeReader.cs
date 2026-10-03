using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;

namespace WinActivityTracker.Core.Services;

public sealed record SystemTimeChange(
    DateTime OldTime,
    DateTime NewTime,
    DateTime OccurredAt,
    int? Reason = null,
    string? ProcessName = null,
    int? ProcessId = null)
{
    /// <summary>
    /// Kernel-General 1 also describes clock synchronization performed while
    /// Windows or a virtual machine is resuming. Those events do not imply that
    /// activity recorded before the gap used the wrong clock, so they must never
    /// become a repair plan.
    /// </summary>
    public bool IsNonActionableForActivityRepair =>
        Reason is 2 or 3 || IsVirtualMachineResumeSync(Reason, ProcessName);

    public string NonActionableDescription => Reason switch
    {
        2 => "系统恢复后与硬件时钟同步，无需修复活动记录",
        3 => "系统时区调整不改变 UTC 活动记录，无需修复",
        _ when IsVirtualMachineResumeSync(Reason, ProcessName) =>
            "虚拟机恢复后的宿主机时间同步，无需修复活动记录",
        _ => "系统时间同步无需修复活动记录"
    };

    internal static bool IsVirtualMachineResumeSync(int? reason, string? processName)
    {
        if (reason != 1 || string.IsNullOrWhiteSpace(processName)) return false;
        var executable = Path.GetFileName(processName);
        return executable.Equals("vmtoolsd.exe", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 系统时间变化的权威记录：Windows 每次 SetSystemTime 都会写
/// Microsoft-Windows-Kernel-General 事件 ID 1（含旧/新时间）。
/// 标准用户可读 System 日志。回查覆盖程序没在跑的时段；watcher 覆盖运行期。
/// </summary>
public class SystemTimeChangeReader
{
    private const string QueryTemplate =
        "*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=1]]";

    public IReadOnlyList<SystemTimeChange> GetChangesSince(DateTime sinceUtc)
    {
        var result = new List<SystemTimeChange>();
        try
        {
            var q = new EventLogQuery("System", PathType.LogName, QueryTemplate)
            { ReverseDirection = true };
            using var events = new EventLogReader(q);
            EventRecord? rec;
            while ((rec = events.ReadEvent()) != null)
            {
                using (rec)
                {
                    if (rec.TimeCreated == null) continue;
                    if (rec.TimeCreated.Value.ToUniversalTime() < sinceUtc) break;
                    var change = ParseEventXml(rec.ToXml());
                    if (change != null) result.Add(change);
                }
            }
        }
        catch (Exception ex)
        {
            // 无日志读取权限或服务不可用：静默降级，由心跳检测兜底
            System.Diagnostics.Trace.WriteLine($"Event log query failed: {ex.Message}");
        }
        result.Reverse();
        return result;
    }

    public IDisposable StartWatching(Action<SystemTimeChange> onChange)
    {
        var watcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, QueryTemplate));
        watcher.EventRecordWritten += (_, e) =>
        {
            try
            {
                var change = ParseEventXml(e.EventRecord.ToXml());
                if (change != null) onChange(change);
            }
            catch { }
        };
        watcher.Enabled = true;
        return watcher;
    }

    /// <summary>纯解析方法，可单测。事件 ID 非 1 或 Data 缺失返回 null。</summary>
    public static SystemTimeChange? ParseEventXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var id = (string?)doc.Descendants(ns + "EventID").FirstOrDefault();
            if (id != "1") return null;

            DateTime? oldTime = null, newTime = null;
            int? reason = null, processId = null;
            string? processName = null;
            foreach (var data in doc.Descendants(ns + "Data"))
            {
                var name = (string?)data.Attribute("Name");
                var value = (string)data;
                if (name == "OldTime") oldTime = DateTime.Parse(value).ToUniversalTime();
                if (name == "NewTime") newTime = DateTime.Parse(value).ToUniversalTime();
                if (name == "Reason" && int.TryParse(value, out var parsedReason)) reason = parsedReason;
                if (name == "ProcessName") processName = value;
                if (name == "ProcessId" && int.TryParse(value, out var parsedPid)) processId = parsedPid;
            }
            if (oldTime == null || newTime == null) return null;
            return new SystemTimeChange(oldTime.Value, newTime.Value,
                ParseTimeCreated(doc, ns)
                ?? (oldTime.Value < newTime.Value ? oldTime.Value : newTime.Value),
                reason, processName, processId);
        }
        catch { return null; }
    }

    /// <summary>
    /// 事件真实发生时刻 = System 节 TimeCreated 的 SystemTime 属性。
    /// 缺失或解析失败返回 null，调用方回退 min(OldTime, NewTime)。
    /// </summary>
    private static DateTime? ParseTimeCreated(XDocument doc, XNamespace ns)
    {
        var systemTime = doc.Descendants(ns + "TimeCreated")
            .Select(tc => (string?)tc.Attribute("SystemTime"))
            .FirstOrDefault(t => t != null);
        if (systemTime != null && DateTime.TryParse(systemTime, out var occurredAt))
            return occurredAt.ToUniversalTime();
        return null;
    }
}
