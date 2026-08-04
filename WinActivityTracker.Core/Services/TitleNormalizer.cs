using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinActivityTracker.Core.Services;

public class TitleNormalizer
{
    private readonly string _filePath;
    private readonly object _reloadLock = new();
    private DateTime _lastWriteTime;
    private Dictionary<string, TitleRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    public string? ConfigError { get; private set; }

    public TitleNormalizer(AppPaths? appPaths = null)
    {
        var dir = AppPaths.ResolveConfigDir(appPaths);
        _filePath = Path.Combine(dir, "title_rules.json");

        if (!File.Exists(_filePath))
        {
            var resourceName = LocaleHelper.IsZh()
                ? "default_title_rules.zh.json"
                : "default_title_rules.en.json";
            var json = EmbeddedResourceReader.ReadAsString(resourceName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, json);
        }

        _rules = LoadFromFile();
        _lastWriteTime = File.GetLastWriteTimeUtc(_filePath);
    }

    public List<TitleRule> GetRules()
    {
        ReloadIfChanged();
        return _rules.Values.ToList();
    }

    public string Apply(string process, string title)
    {
        ReloadIfChanged();
        if (!_rules.TryGetValue(process, out var rule)) return title;
        if (!rule.ApplyOnWrite) return title;
        return rule.Apply(title) ?? title;
    }

    // Snapshot for hot paths (e.g. API responses over up to 50k rows):
    // rules are applied without the ApplyOnWrite gate and without per-row
    // file-time checks; safe because LoadFromFile never mutates old rule objects.
    public TitleSnapshot CreateSnapshot()
    {
        ReloadIfChanged();
        lock (_reloadLock)
        {
            return new TitleSnapshot(_rules);
        }
    }

    public sealed class TitleSnapshot
    {
        private readonly Dictionary<string, TitleRule> _rules;

        internal TitleSnapshot(Dictionary<string, TitleRule> rules) => _rules = rules;

        public string Apply(string process, string title)
        {
            if (!_rules.TryGetValue(process, out var rule)) return title;
            return rule.Apply(title) ?? title;
        }
    }

    public class TitleRule
    {
        public string? Process { get; set; }
        public string? Title { get; set; }
        public string? TitleRegex { get; set; }
        public string? TitleReplacement { get; set; }
        public bool ApplyOnWrite { get; set; } = false;

        [JsonIgnore]
        public string? ErrorMessage { get; set; }

        [JsonIgnore]
        private Regex? _compiledRegex;

        internal string? Apply(string originalTitle)
        {
            if (!string.IsNullOrEmpty(Title))
                return Title;

            if (!string.IsNullOrEmpty(TitleRegex) && !string.IsNullOrEmpty(TitleReplacement))
            {
                var regex = _compiledRegex ??= TryCompileRegex();
                if (regex == null) return null;

                try { return regex.Replace(originalTitle, TitleReplacement); }
                catch (RegexMatchTimeoutException)
                {
                    ErrorMessage = $"Regex timed out in rule '{Process}'";
                    return null;
                }
            }

            return null;
        }

        private Regex? TryCompileRegex()
        {
            try
            {
                return new Regex(TitleRegex!, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
            }
            catch (RegexParseException ex)
            {
                ErrorMessage = $"Regex error in rule '{Process}': {ex.Message}";
                return null;
            }
        }
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
            ConfigError = I18nService._("error.configParseFailed", "title_rules.json", ex.Message);
        }
    }

    private Dictionary<string, TitleRule> LoadFromFile()
    {
        var result = new Dictionary<string, TitleRule>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_filePath)) return result;

        var rules = JsonSerializer.Deserialize<List<TitleRule>>(File.ReadAllText(_filePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (rules != null)
        {
            foreach (var r in rules)
            {
                if (string.IsNullOrEmpty(r.Process)) continue;
                if (string.IsNullOrEmpty(r.Title) && string.IsNullOrEmpty(r.TitleRegex)) continue;
                r.ErrorMessage = null;
                result[r.Process] = r;
            }
        }
        return result;
    }
}
