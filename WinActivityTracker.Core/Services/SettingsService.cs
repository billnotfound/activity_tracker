using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WinActivityTracker.Core.Models;

namespace WinActivityTracker.Core.Services;

public class SettingsService
{
    private readonly string _filePath;
    private TrackerSettings _settings = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly object _lock = new();

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

    // Property names that can appear in settings.json (PascalCase).
    // Used to detect new properties added in program updates.
    private static readonly HashSet<string> KnownPropertyNames = new()
    {
        "TrackingEnabled", "FullscreenBypassIdle", "MergeSameProcessSwitches",
        "WindowPollSeconds", "ProcessPollSeconds", "MediaPollSeconds",
        "IdleThresholdMinutes", "ExcludedProcesses", "DataRetentionDays",
        "AutoCleanup", "ApiPort", "AutoStartEnabled"
    };

    public TrackerSettings Settings => _settings;

    public SettingsService(AppPaths? appPaths = null, string? directoryPath = null)
    {
        var dir = AppPaths.ResolveConfigDir(appPaths, directoryPath);
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
                var raw = File.ReadAllText(_filePath);
                var cleanJson = string.Join("\n", raw.Split('\n')
                    .Select(l => l.TrimStart())
                    .Where(l => !l.StartsWith("//")));
                _settings = JsonSerializer.Deserialize<TrackerSettings>(cleanJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                // Detect properties present in our code but missing from the file.
                var fileKeys = ExtractJsonKeys(cleanJson);
                var missingKeys = KnownPropertyNames
                    .Where(k => !fileKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (missingKeys.Count > 0)
                {
                    // Merge: fill missing properties with defaults.
                    var defaults = new TrackerSettings();
                    foreach (var key in missingKeys)
                        ApplyDefault(key, defaults);

                    // Write with an update banner at the top.
                    var dateMark = DateTime.Now.ToString("yyyy-MM-dd");
                    var keyList = string.Join(", ", missingKeys);
                    var banner = $"// ===== 版本更新 {dateMark} — 新增设置: {keyList} =====";
                    Save(banner);
                }
            }
            else
            {
                _settings = new TrackerSettings();
                Save();
            }
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Failed to parse settings.json: {ex.Message}. Using defaults.");
            _settings = new TrackerSettings();
            SaveDefaults();
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Failed to read settings.json: {ex.Message}. Using defaults.");
            _settings = new TrackerSettings();
            SaveDefaults();
        }
    }

    /// <summary>
    /// Applies the default value for a single property key to _settings.
    /// Mirrors the property list in TrackerSettings.
    /// </summary>
    private void ApplyDefault(string key, TrackerSettings defaults)
    {
        switch (key)
        {
            case "TrackingEnabled": _settings.TrackingEnabled = defaults.TrackingEnabled; break;
            case "FullscreenBypassIdle": _settings.FullscreenBypassIdle = defaults.FullscreenBypassIdle; break;
            case "MergeSameProcessSwitches": _settings.MergeSameProcessSwitches = defaults.MergeSameProcessSwitches; break;
            case "WindowPollSeconds": _settings.WindowPollSeconds = defaults.WindowPollSeconds; break;
            case "ProcessPollSeconds": _settings.ProcessPollSeconds = defaults.ProcessPollSeconds; break;
            case "MediaPollSeconds": _settings.MediaPollSeconds = defaults.MediaPollSeconds; break;
            case "IdleThresholdMinutes": _settings.IdleThresholdMinutes = defaults.IdleThresholdMinutes; break;
            case "ExcludedProcesses": _settings.ExcludedProcesses = defaults.ExcludedProcesses; break;
            case "DataRetentionDays": _settings.DataRetentionDays = defaults.DataRetentionDays; break;
            case "AutoCleanup": _settings.AutoCleanup = defaults.AutoCleanup; break;
            case "ApiPort": _settings.ApiPort = defaults.ApiPort; break;
            case "AutoStartEnabled": _settings.AutoStartEnabled = defaults.AutoStartEnabled; break;
        }
    }

    /// <summary>
    /// Extracts top-level JSON property names from a JSON string.
    /// Handles both PascalCase and camelCase.
    /// </summary>
    private static HashSet<string> ExtractJsonKeys(string json)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = json.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length > 2 && trimmed[0] == '"')
            {
                var endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 1)
                    keys.Add(trimmed.Substring(1, endQuote - 1));
            }
        }
        return keys;
    }

    public void Save(string? headerBanner = null)
    {
        lock (_lock)
        {
            var newJson = JsonSerializer.Serialize(_settings, _jsonOptions);
            var newCommented = InjectComments(newJson);
            if (headerBanner != null)
                newCommented = headerBanner + "\n" + newCommented;

            // Compare data with existing file — skip write if identical.
            if (File.Exists(_filePath))
            {
                var oldData = StripComments(File.ReadAllText(_filePath));
                var newData = StripComments(newCommented);
                if (oldData == newData) return;

                // Keep a backup before overwriting.
                var bak = _filePath + ".bak";
                try { File.Copy(_filePath, bak, overwrite: true); } catch { }
            }

            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, newCommented);
            File.Move(tmp, _filePath, overwrite: true);
        }
    }

    private static string StripComments(string text)
    {
        var lines = text.Split('\n');
        var kept = lines
            .Where(l => !l.TrimStart().StartsWith("//"))
            .Select(l => l.TrimEnd('\r', '\n').TrimEnd());
        return string.Join("", kept.Where(l => l.Length > 0));
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
        lock (_lock)
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

    public void SetTrackingEnabled(bool value)
    {
        lock (_lock)
        {
            _settings.TrackingEnabled = value;
            Save();
        }
    }

    public void SetAutoStartEnabled(bool value)
    {
        lock (_lock)
        {
            _settings.AutoStartEnabled = value;
            Save();
        }
    }

    private void SaveDefaults()
    {
        try { Save(); }
        catch { /* Last-resort recovery: if even atomic save fails, give up silently. */ }
    }
}
