using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// 参照源调度：启动 ~10s 后一次、再 30 分钟后一次；TimeAnomalyService 检测到
/// 整小时可疑偏移时可 RequestCheckAsync() 按需补查。失败静默，下次再试。
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

    public async Task<TimeReferenceResult?> RequestCheckAsync()
    {
        if (!_settings.Settings.UseNtp) return null;
        if (!await _gate.WaitAsync(0)) return LastResult; // 已有查询进行中
        try
        {
            var result = await (_injected ?? CreateReference()).QueryAsync(CancellationToken.None);
            if (result.Succeeded)
            {
                LastResult = result;
                LastQueryAt = DateTime.UtcNow;
                ResultArrived?.Invoke(result);
            }
            else _logger.LogInformation("Time reference query failed: {Source}", result.SourceName);
            return LastResult;
        }
        finally { _gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            await RequestCheckAsync();
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            await RequestCheckAsync();
        }
        catch (OperationCanceledException) { }
    }
}
