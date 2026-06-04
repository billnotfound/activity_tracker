// Singleton service that loads/saves TrackerSettings from a commented JSON file.
// Trackers inject this and read Settings on each poll, so PUT /api/settings takes effect immediately.
//
// JSON COMMENTS: settings.json is written with // comment lines so users can understand
// and hand-edit the file in Notepad. On load, comment lines are stripped before parsing.
//
// File path: %LOCALAPPDATA%\WinActivityTracker\settings.json
using System.Text;
using System.Text.Json;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Services;

public class SettingsService
{
    private readonly string _filePath;
    private TrackerSettings _settings = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // The current settings. Trackers read this on every poll cycle.
    // Replacing the entire object reference would break the live-update contract,
    // so Update() mutates fields in-place.
    public TrackerSettings Settings => _settings;

    public SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinActivityTracker");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        Load();
    }

    // Falls back to defaults if the file is missing or corrupted.
    // Comment lines (//) are stripped before JSON parsing.
    // After loading, the file is re-saved to ensure commented format.
    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var lines = File.ReadAllLines(_filePath);
                // Strip // comments — each comment occupies its own line.
                // Inline comments like "key": "value" // comment are NOT supported.
                var jsonLines = lines
                    .Where(line => !line.TrimStart().StartsWith("//"))
                    .ToArray();
                var json = string.Join(Environment.NewLine, jsonLines);
                _settings = JsonSerializer.Deserialize<TrackerSettings>(json) ?? new();
            }
            else
            {
                _settings = new TrackerSettings();
            }
            // Write commented JSON on first run and after migration from old format
            Save();
        }
        catch
        {
            _settings = new TrackerSettings();
        }
    }

    // Writes settings.json with // comment lines before each property.
    // Uses manual StringBuilder formatting rather than JsonSerializer so we can
    // intersperse comments. Numbers/strings are serialized via JsonSerializer for
    // correct escaping.
    public void Save()
    {
        var s = _settings;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  // ===== 追踪控制 =====");
        sb.AppendLine("  // 主开关 — false 时所有追踪器暂停，API 照常运行。相当于\"暂停\"功能。");
        sb.AppendLine($"  \"TrackingEnabled\": {JsonSerializer.Serialize(s.TrackingEnabled)},");
        sb.AppendLine();
        sb.AppendLine("  // ===== 轮询间隔 (秒) =====");
        sb.AppendLine("  // 窗口/焦点追踪轮询间隔。影响焦点切换检测精度。最小 1。");
        sb.AppendLine($"  \"WindowPollSeconds\": {JsonSerializer.Serialize(s.WindowPollSeconds)},");
        sb.AppendLine("  // 后台进程枚举间隔。枚举所有进程较耗性能，不建议低于 5。最小 5。");
        sb.AppendLine($"  \"ProcessPollSeconds\": {JsonSerializer.Serialize(s.ProcessPollSeconds)},");
        sb.AppendLine("  // 媒体播放检测间隔。歌曲切换通常需要 3-5 秒，设太小无意义。最小 1。");
        sb.AppendLine($"  \"MediaPollSeconds\": {JsonSerializer.Serialize(s.MediaPollSeconds)},");
        sb.AppendLine();
        sb.AppendLine("  // ===== 空闲检测 =====");
        sb.AppendLine("  // 超过此分钟数无键鼠操作即判定为空闲，暂停焦点追踪。最小 1。");
        sb.AppendLine($"  \"IdleThresholdMinutes\": {JsonSerializer.Serialize(s.IdleThresholdMinutes)},");
        sb.AppendLine("  // 全屏/最大化窗口是否绕过空闲检测。打游戏/看视频时不会因无操作而暂停追踪。");
        sb.AppendLine($"  \"FullscreenBypassIdle\": {JsonSerializer.Serialize(s.FullscreenBypassIdle)},");
        sb.AppendLine("  // 同进程连续切换是否合并计数。Firefox 切 3 个 tab 只算 1 次切换。");
        sb.AppendLine($"  \"MergeSameProcessSwitches\": {JsonSerializer.Serialize(s.MergeSameProcessSwitches)},");
        sb.AppendLine();
        sb.AppendLine("  // ===== 进程排除 =====");
        sb.AppendLine("  // 不区分大小写。排除的进程不会出现在焦点记录/窗口快照/后台进程/媒体记录中。谨慎操作。");
        sb.AppendLine("  // 示例: [\"explorer\", \"SearchApp\", \"TextInputHost\"]");
        sb.AppendLine($"  \"ExcludedProcesses\": {JsonSerializer.Serialize(s.ExcludedProcesses)},");
        sb.AppendLine();
        sb.AppendLine("  // ===== 数据库 =====");
        sb.AppendLine("  //数据库的默认保留天数。最小 1。");
        sb.AppendLine($"  \"DataRetentionDays\": {JsonSerializer.Serialize(s.DataRetentionDays)},");
        sb.AppendLine();
        sb.AppendLine("  // ===== 服务器 =====");
        sb.AppendLine("  // API 和 Web UI 的监听端口。修改后需重启生效。默认 5200。");
        sb.AppendLine($"  \"ApiPort\": {JsonSerializer.Serialize(s.ApiPort)},");
        sb.AppendLine();
        sb.AppendLine("  // ===== 开机自启 =====");
        sb.AppendLine("  // 启动时从注册表同步。");
        sb.AppendLine($"  \"AutoStartEnabled\": {JsonSerializer.Serialize(s.AutoStartEnabled)}");
        sb.AppendLine("}");

        File.WriteAllText(_filePath, sb.ToString());
    }

    // Applies new settings with safety floors:
    // - TrackingEnabled: passthrough (no floor)
    // - Window poll min 1s to avoid CPU saturation
    // - Process poll min 5s (enumeration is expensive with hundreds of processes)
    // - Media poll min 1s
    // - Idle threshold min 1 minute
    // - Retention min 1 day (0 deletes all data)
    // - ApiPort clamped to 1024-65535 range (well-known ports excluded)
    public void Update(TrackerSettings newSettings)
    {
        _settings.TrackingEnabled = newSettings.TrackingEnabled;
        _settings.FullscreenBypassIdle = newSettings.FullscreenBypassIdle;
        _settings.MergeSameProcessSwitches = newSettings.MergeSameProcessSwitches;
        _settings.WindowPollSeconds = Math.Max(1, newSettings.WindowPollSeconds);
        _settings.ProcessPollSeconds = Math.Max(5, newSettings.ProcessPollSeconds);
        _settings.MediaPollSeconds = Math.Max(1, newSettings.MediaPollSeconds);
        _settings.IdleThresholdMinutes = Math.Max(1, newSettings.IdleThresholdMinutes);
        _settings.ExcludedProcesses = newSettings.ExcludedProcesses ?? [];
        _settings.DataRetentionDays = Math.Max(1, newSettings.DataRetentionDays);
        _settings.AutoCleanup = newSettings.AutoCleanup;
        _settings.ApiPort = Math.Clamp(newSettings.ApiPort, 1024, 65535);
        _settings.AutoStartEnabled = newSettings.AutoStartEnabled;
        Save();
    }
}
