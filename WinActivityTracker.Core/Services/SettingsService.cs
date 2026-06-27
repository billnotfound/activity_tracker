using System.Reflection;
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
            "// API 和 Web UI 的监听端口。修改后需重启生效。默认 32579。"
        },
        ["AutoCleanup"] = new[] {
            "// 预留：true 时定期自动清理。尚未启用。"
        },
        ["AutoStartEnabled"] = new[] {
            "// ===== 开机自启 =====",
            "// 启动时从注册表同步。"
        },
        ["PressureThrottlingEnabled"] = new[] {
            "// ===== 写入压力节流 =====",
            "// 是否启用写入压力节流。系统 I/O 压力大时自动跳过非核心写入。"
        },
        ["PressureElevatedFillPercent"] = new[] {
            "// WriteQueue 通道填充百分比阈值 —— 触发高压力模式。范围 20-90，默认 30。"
        },
        ["PressureCriticalFillPercent"] = new[] {
            "// WriteQueue 通道填充百分比阈值 —— 触发严重压力模式。范围 50-95，默认 70。"
        },
        ["PressureElevatedLatencySec"] = new[] {
            "// 最近一次成功刷盘距今秒数阈值 —— 触发高压力模式。最小 1，默认 3。"
        },
        ["PressureCriticalLatencySec"] = new[] {
            "// 最近一次成功刷盘距今秒数阈值 —— 触发严重压力模式。最小 5，默认 10。"
        }
    };

    // Derived from TrackerSettings via reflection — always in sync.
    private static readonly HashSet<string> KnownPropertyNames = new(
        typeof(TrackerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name));

    // Property → (min, max) for integer settings. Only add properties that need clamping.
    private static readonly Dictionary<string, (int Min, int Max)> IntConstraints = new()
    {
        ["WindowPollSeconds"] = (1, int.MaxValue),
        ["ProcessPollSeconds"] = (5, int.MaxValue),
        ["MediaPollSeconds"] = (1, int.MaxValue),
        ["IdleThresholdMinutes"] = (1, int.MaxValue),
        ["DataRetentionDays"] = (1, int.MaxValue),
        ["ApiPort"] = (1024, 65535),
        ["PressureElevatedFillPercent"] = (20, 90),
        ["PressureCriticalFillPercent"] = (50, 95),
        ["PressureElevatedLatencySec"] = (1, int.MaxValue),
        ["PressureCriticalLatencySec"] = (5, int.MaxValue),
    };

    private static readonly PropertyInfo[] SettingProperties = typeof(TrackerSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite)
        .ToArray();

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
                    var defaults = new TrackerSettings();
                    foreach (var key in missingKeys)
                        ApplyDefault(key, defaults);

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

    private void ApplyDefault(string key, TrackerSettings defaults)
    {
        var prop = typeof(TrackerSettings).GetProperty(key,
            BindingFlags.Public | BindingFlags.Instance);
        if (prop?.CanWrite == true)
            prop.SetValue(_settings, prop.GetValue(defaults));
    }

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

            if (File.Exists(_filePath))
            {
                var oldData = StripComments(File.ReadAllText(_filePath));
                var newData = StripComments(newCommented);
                if (oldData == newData) return;

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

    public void Update(JsonElement json)
    {
        lock (_lock)
        {
            var propMap = SettingProperties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var jsonProp in json.EnumerateObject())
            {
                if (!propMap.TryGetValue(jsonProp.Name, out var prop))
                    continue;

                object? value;
                if (prop.PropertyType == typeof(int))
                {
                    value = jsonProp.Value.GetInt32();
                    if (IntConstraints.TryGetValue(prop.Name, out var range))
                        value = Math.Clamp((int)value, range.Min, range.Max);
                }
                else if (prop.PropertyType == typeof(bool))
                {
                    value = jsonProp.Value.GetBoolean();
                }
                else if (prop.PropertyType == typeof(List<string>))
                {
                    if (jsonProp.Value.ValueKind == JsonValueKind.Null)
                        value = new List<string>();
                    else
                        value = JsonSerializer.Deserialize<List<string>>(jsonProp.Value.GetRawText())
                                 ?? new List<string>();
                }
                else
                {
                    continue;
                }

                prop.SetValue(_settings, value);
            }
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
