using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinActivityTracker.Core.Services;

public class TagService
{
    private readonly string _filePath;
    private readonly object _reloadLock = new();
    private DateTime _lastWriteTime;
    private List<TagRule> _rules;

    /// <summary>
    /// Non-null when the last reload attempt failed. Exposed for UI feedback.
    /// </summary>
    public string? ConfigError { get; private set; }

    public record TagRule(string Tag, string? Process, string? TitlePattern);

    public TagService(string? directoryPath = null)
    {
        var dir = directoryPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinActivityTracker");
        _filePath = Path.Combine(dir, "tags.json");

        if (!File.Exists(_filePath))
        {
            var defaults = new[]
            {
                new TagRule("代码", "devenv.exe", null),
                new TagRule("代码", "code.exe",    null),
                new TagRule("游戏", "steam.exe",   null),
                new TagRule("视频", null,          "*YouTube*"),
                new TagRule("视频", null,          "*bilibili*"),
                new TagRule("小说", null,          "*晋江*"),
            };
            var json = JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, json);
        }

        _rules = LoadFromFile();
        _lastWriteTime = File.GetLastWriteTimeUtc(_filePath);
    }

    public string? ResolveTag(string processName, string? windowTitle)
    {
        ReloadIfChanged();
        return Match(processName, windowTitle);
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
        PropertyNameCaseInsensitive = true
    };

    private List<TagRule> LoadFromFile()
    {
        var raw = JsonSerializer.Deserialize<List<TagRule>>(File.ReadAllText(_filePath), _jsonOptions);
        return (raw ?? [])
            .Where(r => !string.IsNullOrEmpty(r.Tag)
                && (!string.IsNullOrEmpty(r.Process) || !string.IsNullOrEmpty(r.TitlePattern)))
            .ToList();
    }

    private string? Match(string processName, string? windowTitle)
    {
        // Pass 1: rules with titlePattern — title-based matches take priority
        foreach (var rule in _rules)
        {
            if (string.IsNullOrEmpty(rule.TitlePattern)) continue;
            if (string.IsNullOrEmpty(windowTitle)) continue;
            if (!WildcardMatch(rule.TitlePattern, windowTitle)) continue;

            if (string.IsNullOrEmpty(rule.Process)
                || string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase))
            {
                return rule.Tag;
            }
        }

        // Pass 2: process-only rules (no titlePattern)
        foreach (var rule in _rules)
        {
            if (!string.IsNullOrEmpty(rule.TitlePattern)) continue;
            if (string.IsNullOrEmpty(rule.Process)) continue;
            if (string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase))
            {
                return rule.Tag;
            }
        }

        return null;
    }

    private static bool WildcardMatch(string pattern, string input)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
    }
}
