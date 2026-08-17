using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;

namespace WinActivityTracker.Core.Services;

public sealed record SystemTimeChange(DateTime OldTime, DateTime NewTime, DateTime OccurredAt);

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
            { ReverseDirection = false };
            var events = new EventLogReader(q);
            EventRecord? rec;
            while ((rec = events.ReadEvent()) != null)
            {
                using (rec)
                {
                    if (rec.TimeCreated == null || rec.TimeCreated.Value.ToUniversalTime() < sinceUtc)
                        continue;
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
            foreach (var data in doc.Descendants(ns + "Data"))
            {
                var name = (string?)data.Attribute("Name");
                if (name == "OldTime") oldTime = DateTime.Parse((string)data).ToUniversalTime();
                if (name == "NewTime") newTime = DateTime.Parse((string)data).ToUniversalTime();
            }
            if (oldTime == null || newTime == null) return null;
            return new SystemTimeChange(oldTime.Value, newTime.Value,
                ParseTimeCreated(doc, ns)
                ?? (oldTime.Value < newTime.Value ? oldTime.Value : newTime.Value));
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
