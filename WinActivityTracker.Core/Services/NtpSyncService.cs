using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// 参照源调度：启动约 10 秒后查询，此后每 30 分钟查询一次；TimeAnomalyService
/// 检测到整小时可疑偏移时可 RequestCheckAsync() 按需补查。
/// </summary>
public class NtpSyncService : BackgroundService
{
    private readonly SettingsService _settings;
    private readonly ILogger<NtpSyncService> _logger;
    private readonly ITimeReference? _injected;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TimeReferenceResult? LastResult { get; private set; }
    public DateTime? LastQueryAt { get; private set; }

    /// <summary>成功写入新结果后触发（TimeAnomalyService 借此重评 Pending 异常）。</summary>
    public event Action<TimeReferenceResult>? ResultArrived;

    /// <param name="injected">可注入参照（供测试用假参照）；null 时按设置创建真实参照。</param>
    public NtpSyncService(SettingsService settings, ILogger<NtpSyncService> logger, ITimeReference? injected = null)
    {
        _settings = settings;
        _logger = logger;
        _injected = injected;
    }

    public ITimeReference CreateReference()
    {
        var s = _settings.Settings;
        return s.TimeSourceMode.Equals("Http", StringComparison.OrdinalIgnoreCase)
            ? new HttpTimeReference(s.TimeServer)
            : new SntpTimeReference(s.TimeServer);
    }

    public async Task<TimeReferenceResult?> RequestCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Settings.UseNtp) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var result = await (_injected ?? CreateReference()).QueryAsync(cancellationToken);
            LastQueryAt = DateTime.UtcNow;
            if (result.Succeeded)
            {
                LastResult = result;
                ResultArrived?.Invoke(result);
            }
            else _logger.LogInformation("Time reference query failed: {Source}", result.SourceName);
            return result;
        }
        finally { _gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunScheduleAsync(
                () => RequestCheckAsync(stoppingToken),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMinutes(30),
                stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    internal static async Task RunScheduleAsync(Func<Task> checkAsync,
        TimeSpan initialDelay, TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(initialDelay, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                await checkAsync();
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
