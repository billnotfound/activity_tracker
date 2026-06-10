// Loads title_rules.json from %LOCALAPPDATA%\WinActivityTracker\.
// Each rule: { "process": "firefox", "title": "Mozilla Firefox" }
// When a process matches, its window title is replaced with the canonical title,
// collapsing all tabs/windows into a single session.
//
// Hot-reload: checks File.GetLastWriteTimeUtc on each call.
using System.Text.Json;

namespace WinActivityTracker.Core.Services;

public class TitleNormalizer
{
    private readonly string _filePath;
    private readonly object _reloadLock = new();
    private DateTime _lastWriteTime;
    private Dictionary<string, string> _rules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Non-null when the last reload attempt failed. Exposed for UI feedback.
    /// </summary>
    public string? ConfigError { get; private set; }

    public TitleNormalizer()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinActivityTracker");
        _filePath = Path.Combine(dir, "title_rules.json");

        if (!File.Exists(_filePath))
        {
            var defaults = new[]
            {
                new { process = "firefox", title = "Mozilla Firefox" },
                new { process = "msedge", title = "Microsoft Edge" },
                new { process = "chrome", title = "Google Chrome" },
                new { process = "WindowsTerminal", title = "Windows Terminal" },
                new { process = "ApplicationFrameHost", title = "[UWP App]" },
            };
            var json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, json);
        }

        _rules = LoadFromFile();
        _lastWriteTime = File.GetLastWriteTimeUtc(_filePath);
    }

    public string Normalize(string processName, string originalTitle)
    {
        ReloadIfChanged();
        if (_rules.TryGetValue(processName, out var canonical))
            return canonical;
        return originalTitle;
    }

    public List<TitleRule> GetRules()
    {
        ReloadIfChanged();
        return _rules.Select(kv => new TitleRule(kv.Key, kv.Value)).ToList();
    }

    public record TitleRule(string process, string title);

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

    private Dictionary<string, string> LoadFromFile()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_filePath)) return result;

        var rules = JsonSerializer.Deserialize<List<TitleRule>>(File.ReadAllText(_filePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (rules != null)
            foreach (var r in rules)
                if (!string.IsNullOrEmpty(r.process) && !string.IsNullOrEmpty(r.title))
                    result[r.process] = r.title;
        return result;
    }
}
