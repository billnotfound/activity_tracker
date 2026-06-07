using System.Text;
using System.Text.Json;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Services;

public class SettingsService
{
    private readonly string _filePath;
    private TrackerSettings _settings = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // Map of JSON property paths to their // comment lines.
    // Add entries here when adding new properties to TrackerSettings.
    private static readonly Dictionary<string, string[]> PropertyComments = new()
    {
        ["TrackingEnabled"] = new[] {
            "// ===== 追踪控制 =====",
            "// 主开关 — false 时所有追踪器暂停，API 照常运行。相当于\"暂停\"功能。"
        },
        ["WindowPollSeconds"] = new[] {
            "// ===== 轮询间隔 (秒) =====",
            "// 窗口/焦点追踪轮询间隔。影响焦点切换检测精度。最小 1。"
        },
        ["ProcessPollSeconds"] = new[] {
            "// 后台进程枚举间隔。枚举所有进程较耗性能，不建议低于 5。最小 5。"
        },
        ["MediaPollSeconds"] = new[] {
            "// 媒体播放检测间隔。歌曲切换通常需要 3-5 秒，设太小无意义。最小 1。"
        },
        ["IdleThresholdMinutes"] = new[] {
            "// ===== 空闲检测 =====",
            "// 超过此分钟数无键鼠操作即判定为空闲，暂停焦点追踪。最小 1。"
        },
        ["FullscreenBypassIdle"] = new[] {
            "// 全屏/最大化窗口是否绕过空闲检测。打游戏/看视频时不会因无操作而暂停追踪。"
        },
        ["MergeSameProcessSwitches"] = new[] {
            "// 同进程连续切换是否合并计数。浏览器 切 3 个 tab 只算 1 次切换。"
        },
        ["ExcludedProcesses"] = new[] {
            "// ===== 进程排除 =====",
            "// 不区分大小写。排除的进程不会出现在焦点记录/窗口快照/后台进程/媒体记录中。",
            "// 示例: [\"explorer\", \"SearchApp\", \"TextInputHost\"]"
        },
        ["DataRetentionDays"] = new[] {
            "// ===== 数据库 =====",
            "// 数据库的默认保留天数。最小 1。"
        },
        ["ApiPort"] = new[] {
            "// ===== 服务器 =====",
            "// API 和 Web UI 的监听端口。修改后需重启生效。默认 5200。"
        },
        ["AutoCleanup"] = new[] {
            "// 预留：true 时定期自动清理。尚未启用。"
        },
        ["AutoStartEnabled"] = new[] {
            "// ===== 开机自启 =====",
            "// 启动时从注册表同步。"
        }
    };

    public TrackerSettings Settings => _settings;

    public SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinActivityTracker");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var lines = File.ReadAllLines(_filePath);
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
            Save();
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Failed to parse settings.json: {ex.Message}. Using defaults.");
            _settings = new TrackerSettings();
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Failed to read settings.json: {ex.Message}. Using defaults.");
            _settings = new TrackerSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, _jsonOptions);
        var commented = InjectComments(json);
        File.WriteAllText(_filePath, commented);
    }

    /// <summary>
    /// Serializes with JsonSerializer, then injects // comment lines before known properties.
    /// Unknown properties (newly added to TrackerSettings but not yet in PropertyComments)
    /// are preserved without comments rather than silently dropped.
    /// </summary>
    private static string InjectComments(string json)
    {
        var lines = json.Split('\n');
        var result = new StringBuilder();
        var firstProp = true;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length > 2 && trimmed[0] == '"')
            {
                var endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    var propName = trimmed.Substring(1, endQuote - 1);
                    if (PropertyComments.TryGetValue(propName, out var comments))
                    {
                        if (!firstProp) result.AppendLine();
                        foreach (var c in comments)
                            result.AppendLine(c);
                    }
                }
            }
            firstProp = false;
            result.AppendLine(line.TrimEnd('\r', '\n'));
        }

        return result.ToString().TrimEnd('\r', '\n') + Environment.NewLine;
    }

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
