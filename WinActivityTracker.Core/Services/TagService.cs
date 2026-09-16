using System.Globalization;
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
    /// <summary>
    /// Special internal tag: rules tagged with this hide matching records
    /// globally (all endpoints + status window) and exclude them from totals.
    /// Allowed through the "_"-prefix filter; never returned by ResolveTags.
    /// </summary>
    public const string HiddenTag = "__hidden";

    /// <summary>
    /// Special internal tag: rules tagged with this mark matching records as
    /// idle time. Idle records stay visible but are flagged as idle and are
    /// excluded from activity totals (summary reports them as totalIdleSeconds).
    /// Allowed through the "_"-prefix filter; never returned by ResolveTags.
    /// </summary>
    public const string IdleTag = "__idle";

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
            var resourceName = LocaleHelper.IsZh()
                ? "default_tags.zh.json"
                : "default_tags.en.json";
            var json = EmbeddedResourceReader.ReadAsString(resourceName);
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
        var allMatches = FindAllMatches(processName, windowTitle)
            .Where(r => r.Tag != HiddenTag && r.Tag != IdleTag)
            .ToList();
        return ResolveByWeight(allMatches);
    }

    /// <summary>
    /// Rules tagged with <see cref="HiddenTag"/>: matches hide the record
    /// globally and exclude it from totals.
    /// </summary>
    public List<TagRule> GetHiddenRules()
    {
        ReloadIfChanged();
        return _rules.Where(r => r.Tag == HiddenTag).ToList();
    }

    /// <summary>
    /// Returns true if any hidden rule matches the given process/window.
    /// Static so callers that already fetched rules can reuse them.
    /// </summary>
    public static bool MatchesHidden(
        IReadOnlyList<TagRule> hiddenRules, string processName, string? windowTitle)
    {
        foreach (var rule in hiddenRules)
        {
            if (!string.IsNullOrEmpty(rule.TitlePattern))
            {
                if (string.IsNullOrEmpty(windowTitle)) continue;
                var processOk = string.IsNullOrEmpty(rule.Process)
                    || string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase);
                if (processOk && WildcardMatch(rule.TitlePattern, windowTitle))
                    return true;
            }
            else if (!string.IsNullOrEmpty(rule.Process)
                && string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Rules tagged with <see cref="IdleTag"/>: matches are marked as idle time.
    /// </summary>
    public List<TagRule> GetIdleRules()
    {
        ReloadIfChanged();
        return _rules.Where(r => r.Tag == IdleTag).ToList();
    }

    /// <summary>
    /// Returns true if any idle rule matches the given process/window.
    /// Static so callers that already fetched rules can reuse them.
    /// </summary>
    public static bool MatchesIdle(
        IReadOnlyList<TagRule> idleRules, string processName, string? windowTitle)
    {
        foreach (var rule in idleRules)
        {
            if (!string.IsNullOrEmpty(rule.TitlePattern))
            {
                if (string.IsNullOrEmpty(windowTitle)) continue;
                var processOk = string.IsNullOrEmpty(rule.Process)
                    || string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase);
                if (processOk && WildcardMatch(rule.TitlePattern, windowTitle))
                    return true;
            }
            else if (!string.IsNullOrEmpty(rule.Process)
                && string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Convenience: reloads rules and checks whether the record is idle.
    /// </summary>
    public bool IsIdle(string processName, string? windowTitle)
        => MatchesIdle(GetIdleRules(), processName, windowTitle);

    /// <summary>
    /// Convenience: reloads rules and checks whether the record is hidden.
    /// </summary>
    public bool IsHidden(string processName, string? windowTitle)
        => MatchesHidden(GetHiddenRules(), processName, windowTitle);

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
                && (!r.Tag.StartsWith('_') || r.Tag == HiddenTag || r.Tag == IdleTag)
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
                .OrderBy(r => r.Tag)
                .First();
            return [winner.Tag];
        }
    }

    private static bool WildcardMatch(string pattern, string input)
    {
        // The guided rule builder stores explicit regular expressions with a
        // prefix so existing wildcard-based configurations keep their original
        // meaning. Inline (?i) is supported by .NET and generated by the UI.
        if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Regex.IsMatch(input, pattern[6..], RegexOptions.None,
                    TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
}
