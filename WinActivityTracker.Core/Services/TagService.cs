using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinActivityTracker.Core.Services;

public class TagService
{
    private readonly string _filePath;
    private readonly object _reloadLock = new();
    private DateTime _lastWriteTime;
    private List<TagRule> _rules;

    public string? ConfigError { get; private set; }

    public enum TagMode { Coexist, Overwrite }

    public record TagRule(
        string Tag,
        string? Process,
        string? TitlePattern,
        int Weight = 0,
        TagMode Mode = TagMode.Coexist);

    public TagService(AppPaths? appPaths = null, string? directoryPath = null)
    {
        var dir = directoryPath
            ?? appPaths?.ConfigDir
            ?? Environment.GetEnvironmentVariable("WTA_SETTINGS_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinActivityTracker");
        _filePath = Path.Combine(dir, "tags.json");

        if (!File.Exists(_filePath))
        {
            var defaults = new object[]
            {
                new { _comment = "=== 标签规则格式 ===" },
                new { _comment = "tag: 分类名称（如 代码、游戏、视频）" },
                new { _comment = "process: 进程名（可选），大小写不敏感，如 devenv.exe" },
                new { _comment = "titlePattern: 窗口标题通配符（可选），* 匹配任意字符，如 *YouTube*" },
                new { _comment = "weight: 权重，整数，越大越优先，默认 0" },
                new { _comment = "mode: coexist(共存) 或 overwrite(覆盖)，默认 coexist" },
                new { _comment = "" },
                new { _comment = "=== 权重解析规则 ===" },
                new { _comment = "共存 + 共存 → 全部生效（所有权重相同的共存标签一起返回）" },
                new { _comment = "覆盖 + 覆盖 → 权重高者胜出（同权重取文件中靠前者）" },
                new { _comment = "共存 + 覆盖 → 权重高者决定最终结果（同权重共存优先）" },
                new { _comment = "" },
                new { _comment = "=== 示例规则 ===" },
                new TagRule("代码", "devenv.exe", null, Weight: 5, Mode: TagMode.Overwrite),
                new TagRule("开发", "devenv.exe", null, Weight: 3, Mode: TagMode.Coexist),
                new TagRule("代码", "code.exe", null, Weight: 5, Mode: TagMode.Overwrite),
                new TagRule("游戏", "steam.exe", null, Weight: 10, Mode: TagMode.Overwrite),
                new TagRule("视频", null, "*YouTube*", Weight: 3, Mode: TagMode.Coexist),
                new TagRule("视频", null, "*bilibili*", Weight: 3, Mode: TagMode.Coexist),
                new TagRule("音乐", "spotify.exe", null, Weight: 3, Mode: TagMode.Coexist),
                new TagRule("小说", null, "*晋江*", Weight: 2, Mode: TagMode.Coexist),
            };
            var json = JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                });
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, json);
        }

        _rules = LoadFromFile();
        _lastWriteTime = File.GetLastWriteTimeUtc(_filePath);
    }

    /// <summary>
    /// Resolves all tags for a given process/window, applying weight and mode rules.
    /// Returns an empty list if nothing matches.
    /// </summary>
    public List<string> ResolveTags(string processName, string? windowTitle)
    {
        ReloadIfChanged();
        var allMatches = FindAllMatches(processName, windowTitle);
        return ResolveByWeight(allMatches);
    }

    /// <summary>
    /// Convenience: returns the first resolved tag, or null.
    /// </summary>
    public string? ResolveTag(string processName, string? windowTitle)
    {
        var tags = ResolveTags(processName, windowTitle);
        return tags.Count > 0 ? tags[0] : null;
    }

    public List<TagRule> GetRules()
    {
        ReloadIfChanged();
        return _rules;
    }

    private void ReloadIfChanged()
    {
        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(_filePath);
            if (lastWrite == _lastWriteTime) return;

            lock (_reloadLock)
            {
                lastWrite = File.GetLastWriteTimeUtc(_filePath);
                if (lastWrite == _lastWriteTime) return;

                _rules = LoadFromFile();
                ConfigError = null;
                _lastWriteTime = lastWrite;
            }
        }
        catch (Exception ex)
        {
            ConfigError = $"tags.json 解析失败: {ex.Message}。已沿用旧配置。";
        }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private List<TagRule> LoadFromFile()
    {
        var raw = JsonSerializer.Deserialize<List<TagRule>>(File.ReadAllText(_filePath), _jsonOptions);
        return (raw ?? [])
            .Where(r => !string.IsNullOrEmpty(r.Tag)
                && !r.Tag.StartsWith('_')
                && (!string.IsNullOrEmpty(r.Process) || !string.IsNullOrEmpty(r.TitlePattern)))
            .ToList();
    }

    /// <summary>
    /// Finds all rules that match the given process/window, with title-based
    /// matches taking priority over process-only matches.
    /// </summary>
    private List<TagRule> FindAllMatches(string processName, string? windowTitle)
    {
        var matches = new List<TagRule>();

        // Pass 1: rules with titlePattern matching
        foreach (var rule in _rules)
        {
            if (string.IsNullOrEmpty(rule.TitlePattern)) continue;
            if (string.IsNullOrEmpty(windowTitle)) continue;
            if (!WildcardMatch(rule.TitlePattern, windowTitle)) continue;

            if (string.IsNullOrEmpty(rule.Process)
                || string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(rule);
            }
        }

        if (matches.Count > 0) return matches;

        // Pass 2: process-only rules
        foreach (var rule in _rules)
        {
            if (!string.IsNullOrEmpty(rule.TitlePattern)) continue;
            if (string.IsNullOrEmpty(rule.Process)) continue;
            if (string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(rule);
            }
        }

        return matches;
    }

    /// <summary>
    /// Applies weight-based resolution:
    ///   1. Split matches into coexist and overwrite groups
    ///   2. Find max weight in each group
    ///   3. Higher-weight group wins; on tie, coexist wins
    ///   4. If coexist wins: return all coexist tags at max weight
    ///   5. If overwrite wins: return the single highest-weight overwrite tag
    /// </summary>
    internal static List<string> ResolveByWeight(List<TagRule> matches)
    {
        if (matches.Count == 0) return [];

        var coexist = matches.Where(r => r.Mode == TagMode.Coexist).ToList();
        var overwrite = matches.Where(r => r.Mode == TagMode.Overwrite).ToList();

        var maxCoexist = coexist.Count > 0 ? coexist.Max(r => r.Weight) : int.MinValue;
        var maxOverwrite = overwrite.Count > 0 ? overwrite.Max(r => r.Weight) : int.MinValue;

        if (maxCoexist >= maxOverwrite)
        {
            return coexist
                .Where(r => r.Weight == maxCoexist)
                .Select(r => r.Tag)
                .Distinct()
                .ToList();
        }
        else
        {
            var winner = overwrite
                .Where(r => r.Weight == maxOverwrite)
                .OrderBy(r => r.Tag) // deterministic tiebreak within same-mode same-weight
                .First();
            return [winner.Tag];
        }
    }

    private static bool WildcardMatch(string pattern, string input)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
    }
}
