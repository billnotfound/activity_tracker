using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Core.Services;

public interface ITimeReference
{
    Task<TimeReferenceResult> QueryAsync(CancellationToken ct);
}
