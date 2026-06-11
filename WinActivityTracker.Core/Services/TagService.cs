using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinActivityTracker.Core.Services;

internal static class LocaleHelper
{
    public static bool IsZh() =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
}

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
        var dir = AppPaths.ResolveConfigDir(appPaths, directoryPath);
        _filePath = Path.Combine(dir, "tags.json");

        if (!File.Exists(_filePath))
        {
            var defaults = LocaleHelper.IsZh()
                ? ZhDefaults()
                : EnDefaults();

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
            ConfigError = I18nService._("error.configParseFailed", "tags.json", ex.Message);
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

    private static object[] ZhDefaults() => new object[]
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
        new { _comment = "=== 代码 / 开发 ===" },
        new TagRule("代码", "devenv.exe", null, Weight: 5, Mode: TagMode.Overwrite),
        new TagRule("代码", "code.exe", null, Weight: 5, Mode: TagMode.Overwrite),
        new TagRule("代码", "msbuild.exe", null, Weight: 5, Mode: TagMode.Overwrite),
        new TagRule("代码", "notepad++.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("代码", "sublime_text.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== 游戏 (覆盖、高权重) ===" },
        new TagRule("游戏", "steam.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("游戏", "epicgameslauncher.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("游戏", "valorant.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("游戏", "cs2.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("游戏", "GenshinImpact.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("游戏", "StarRail.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new { _comment = "" },
        new { _comment = "=== 视频 ===" },
        new TagRule("视频", null, "*YouTube*", Weight: 3, Mode: TagMode.Coexist),
        new TagRule("视频", null, "*bilibili*", Weight: 3, Mode: TagMode.Coexist),
        new TagRule("视频", "mpv.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("视频", "vlc.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== 阅读 ===" },
        new TagRule("阅读", null, "*晋江*", Weight: 2, Mode: TagMode.Coexist),
        new TagRule("阅读", null, "*轻小说*", Weight: 2, Mode: TagMode.Coexist),
        new TagRule("阅读", null, "*起点*", Weight: 2, Mode: TagMode.Coexist),
        new TagRule("阅读", null, "*番茄小说*", Weight: 2, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== 音乐 ===" },
        new TagRule("音乐", "spotify.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("音乐", "foobar2000.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("音乐", "cloudmusic.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("音乐", "qqmusic.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== 社交 ===" },
        new TagRule("社交", "wechat.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new TagRule("社交", "qq.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new TagRule("社交", "telegram.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new TagRule("社交", "discord.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== 办公 ===" },
        new TagRule("办公", "winword.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("办公", "excel.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("办公", "powerpnt.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("办公", "wps.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== 折腾 / 系统设置 (覆盖、高权重) ===" },
        new TagRule("折腾", "SystemSettings.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("折腾", "control.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("折腾", "regedit.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("折腾", "taskmgr.exe", null, Weight: 10, Mode: TagMode.Overwrite),
    };

    private static object[] EnDefaults() => new object[]
    {
        new { _comment = "=== Tag Rule Format ===" },
        new { _comment = "tag: category name (e.g. Coding, Gaming, Video)" },
        new { _comment = "process: process name (optional), case-insensitive, e.g. devenv.exe" },
        new { _comment = "titlePattern: window title wildcard (optional), * matches any chars, e.g. *YouTube*" },
        new { _comment = "weight: priority weight (integer), higher wins, default 0" },
        new { _comment = "mode: coexist or overwrite, default coexist" },
        new { _comment = "" },
        new { _comment = "=== Resolution Rules ===" },
        new { _comment = "Coexist + Coexist -> all take effect (all coexist tags at max weight returned together)" },
        new { _comment = "Overwrite + Overwrite -> higher weight wins (first-in-file breaks ties)" },
        new { _comment = "Coexist + Overwrite -> higher weight decides (coexist wins tiebreaker)" },
        new { _comment = "" },
        new { _comment = "=== Coding / Development ===" },
        new TagRule("Coding", "devenv.exe", null, Weight: 5, Mode: TagMode.Overwrite),
        new TagRule("Coding", "code.exe", null, Weight: 5, Mode: TagMode.Overwrite),
        new TagRule("Coding", "msbuild.exe", null, Weight: 5, Mode: TagMode.Overwrite),
        new TagRule("Coding", "notepad++.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Coding", "sublime_text.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Coding", "nvim.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Coding", "vim.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== Gaming (overwrite, high weight) ===" },
        new TagRule("Gaming", "steam.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Gaming", "epicgameslauncher.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Gaming", "valorant.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Gaming", "cs2.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Gaming", "Minecraft.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Gaming", "RobloxPlayerBeta.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new { _comment = "" },
        new { _comment = "=== Video ===" },
        new TagRule("Video", null, "*YouTube*", Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Video", null, "*Netflix*", Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Video", null, "*Twitch*", Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Video", "mpv.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Video", "vlc.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== Reading ===" },
        new TagRule("Reading", null, "*Kindle*", Weight: 2, Mode: TagMode.Coexist),
        new TagRule("Reading", null, "*Royal Road*", Weight: 2, Mode: TagMode.Coexist),
        new TagRule("Reading", "sumatrapdf.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== Music ===" },
        new TagRule("Music", "spotify.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Music", "foobar2000.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Music", "Music.UI.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== Social ===" },
        new TagRule("Social", "discord.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new TagRule("Social", "telegram.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new TagRule("Social", "slack.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new TagRule("Social", "teams.exe", null, Weight: 2, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== Office / Productivity ===" },
        new TagRule("Office", "winword.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Office", "excel.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Office", "powerpnt.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Office", "outlook.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new TagRule("Office", "notion.exe", null, Weight: 3, Mode: TagMode.Coexist),
        new { _comment = "" },
        new { _comment = "=== Tinkering / System Settings (overwrite, high weight) ===" },
        new TagRule("Tinkering", "SystemSettings.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Tinkering", "control.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Tinkering", "regedit.exe", null, Weight: 10, Mode: TagMode.Overwrite),
        new TagRule("Tinkering", "taskmgr.exe", null, Weight: 10, Mode: TagMode.Overwrite),
    };
}
