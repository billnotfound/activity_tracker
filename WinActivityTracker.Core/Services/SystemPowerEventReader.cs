using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Services;

public sealed record SystemOffPeriod(string EventType, DateTime Start, DateTime End);
public sealed record SystemPowerLogResult(bool Succeeded, IReadOnlyList<SystemOffPeriod> Periods);

/// <summary>
/// Reads authoritative sleep/resume and shutdown/start intervals from the
/// Windows System event log. Heartbeat uses this to distinguish a real power
/// transition from an ordinary delayed background-service iteration.
/// </summary>
public class SystemPowerEventReader
{
    private const string SleepQuery =
        "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]";
    private const string ShutdownQuery =
        "*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=13]]";
    private const string StartQuery =
        "*[System[Provider[@Name='EventLog'] and EventID=6005]]";

    public SystemPowerLogResult GetOffPeriods(DateTime sinceUtc, DateTime untilUtc)
    {
        try
        {
            var periods = new List<SystemOffPeriod>();
            foreach (var xml in ReadXmlEvents(SleepQuery, sinceUtc, untilUtc))
            {
                var period = ParseSleepEventXml(xml);
                if (period != null && period.End > sinceUtc && period.Start < untilUtc)
                    periods.Add(period);
            }

            var shutdowns = ReadEventTimes(ShutdownQuery, sinceUtc, untilUtc);
            var starts = ReadEventTimes(StartQuery, sinceUtc, untilUtc.AddMinutes(5));
            periods.AddRange(PairShutdownEvents(shutdowns, starts, untilUtc));

            return new SystemPowerLogResult(true, periods
                .Where(period => period.End > period.Start)
                .OrderBy(period => period.Start)
                .ToList());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Power event log query failed: {ex.Message}");
            return new SystemPowerLogResult(false, Array.Empty<SystemOffPeriod>());
        }
    }

    private static IEnumerable<string> ReadXmlEvents(
        string query, DateTime sinceUtc, DateTime untilUtc)
    {
        var q = new EventLogQuery("System", PathType.LogName, query)
        { ReverseDirection = true };
        using var reader = new EventLogReader(q);
        EventRecord? record;
        while ((record = reader.ReadEvent()) != null)
        {
            using (record)
            {
                if (!record.TimeCreated.HasValue) continue;
                var created = record.TimeCreated.Value.ToUniversalTime();
                if (created < sinceUtc) break;
                if (created > untilUtc.AddMinutes(5)) continue;
                yield return record.ToXml();
            }
        }
    }

    private static List<DateTime> ReadEventTimes(
        string query, DateTime sinceUtc, DateTime untilUtc)
    {
        var result = new List<DateTime>();
        var q = new EventLogQuery("System", PathType.LogName, query)
        { ReverseDirection = true };
        using var reader = new EventLogReader(q);
        EventRecord? record;
        while ((record = reader.ReadEvent()) != null)
        {
            using (record)
            {
                if (!record.TimeCreated.HasValue) continue;
                var created = record.TimeCreated.Value.ToUniversalTime();
                if (created < sinceUtc) break;
                if (created <= untilUtc) result.Add(created);
            }
        }
        result.Sort();
        return result;
    }

    public static SystemOffPeriod? ParseSleepEventXml(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            DateTime? sleep = null;
            DateTime? wake = null;
            foreach (var data in document.Descendants(ns + "Data"))
            {
                var name = (string?)data.Attribute("Name");
                if (name == "SleepTime" && DateTime.TryParse((string?)data, out var value))
                    sleep = value.ToUniversalTime();
                else if (name == "WakeTime" && DateTime.TryParse((string?)data, out value))
                    wake = value.ToUniversalTime();
            }
            if (!sleep.HasValue || !wake.HasValue || wake <= sleep) return null;
            return new SystemOffPeriod(SystemEventTypes.Sleep, sleep.Value, wake.Value);
        }
        catch
        {
            return null;
        }
    }

    internal static IReadOnlyList<SystemOffPeriod> PairShutdownEvents(
        IEnumerable<DateTime> shutdownTimes,
        IEnumerable<DateTime> startTimes,
        DateTime fallbackEnd)
    {
        var starts = startTimes.OrderBy(value => value).ToList();
        return shutdownTimes.OrderBy(value => value)
            .Select(shutdown => new SystemOffPeriod(
                SystemEventTypes.Shutdown,
                shutdown,
                starts.FirstOrDefault(start => start > shutdown) is var start && start != default
                    ? start
                    : fallbackEnd))
            .Where(period => period.End > period.Start)
            .ToList();
    }
}
