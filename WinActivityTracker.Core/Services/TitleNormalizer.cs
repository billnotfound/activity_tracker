// Loads title_rules.json from %LOCALAPPDATA%\WinActivityTracker\.
//
// Two rule types:
//   1. Fixed replacement:  { "process": "firefox", "title": "Mozilla Firefox" }
//   2. Regex substitution: { "process": "fish", "titleRegex": "^(\\S+)", "titleReplacement": "$1" }
//
// Type 1 takes priority if both are specified.
// Hot-reload: checks File.GetLastWriteTimeUtc on each call.
using System.Text.Encodings.Web;
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

    public TitleNormalizer()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinActivityTracker");
        _filePath = Path.Combine(dir, "title_rules.json");

        if (!File.Exists(_filePath))
        {
            var defaults = new[]
            {
                new TitleRule { Process = "firefox",            Title = "Mozilla Firefox" },
                new TitleRule { Process = "msedge",             Title = "Microsoft Edge" },
                new TitleRule { Process = "chrome",             Title = "Google Chrome" },
                new TitleRule { Process = "WindowsTerminal",    Title = "Windows Terminal" },
                new TitleRule { Process = "ApplicationFrameHost", Title = "[UWP App]" },
                new TitleRule { Process = "fish", TitleRegex = @"^(\S+)", TitleReplacement = "$1" },
                new TitleRule { Process = "bash", TitleRegex = @"^(\S+)", TitleReplacement = "$1" },
            };
            var json = JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, json);
        }

        _rules = LoadFromFile();
        _lastWriteTime = File.GetLastWriteTimeUtc(_filePath);
    }

    /// <summary>
    /// Normalizes a window title. If a rule matches the process name:
    ///   - If Title is set: returns Title (full replacement)
    ///   - If TitleRegex + TitleReplacement are set: returns regex substitution
    ///   - Otherwise returns the original title unchanged
    /// </summary>
    public string Normalize(string processName, string originalTitle)
    {
        ReloadIfChanged();
        if (_rules.TryGetValue(processName, out var rule))
        {
            var result = rule.Apply(originalTitle);
            if (result != null)
                return result;
        }
        return originalTitle;
    }

    public List<TitleRule> GetRules()
    {
        ReloadIfChanged();
        return _rules.Values.ToList();
    }

    public class TitleRule
    {
        public string? Process { get; set; }
        public string? Title { get; set; }
        public string? TitleRegex { get; set; }
        public string? TitleReplacement { get; set; }

        [JsonIgnore]
        public string? ErrorMessage { get; set; }

        internal string? Apply(string originalTitle)
        {
            // Fixed title takes priority
            if (!string.IsNullOrEmpty(Title))
                return Title;

            // Regex substitution
            if (!string.IsNullOrEmpty(TitleRegex) && !string.IsNullOrEmpty(TitleReplacement))
            {
                try { return Regex.Replace(originalTitle, TitleRegex, TitleReplacement); }
                catch (RegexParseException ex)
                {
                    ErrorMessage = $"Regex error in rule '{Process}': {ex.Message}";
                    return null;
                }
            }

            return null;
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
            ConfigError = $"title_rules.json 解析失败: {ex.Message}。已沿用旧配置。";
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
