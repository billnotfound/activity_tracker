// Loads title_rules.json from %LOCALAPPDATA%\WinActivityTracker\.
// Each rule: { "process": "firefox", "title": "Mozilla Firefox" }
// When a process matches, its window title is replaced with the canonical title,
// collapsing all tabs/windows into a single session.
using System.Text.Json;

namespace WinActivityTracker.Core.Services;

public class TitleNormalizer
{
    private readonly Dictionary<string, string> _rules = new(StringComparer.OrdinalIgnoreCase);

    public TitleNormalizer()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinActivityTracker");
        var path = Path.Combine(dir, "title_rules.json");

        if (!File.Exists(path))
        {
            // Write default rules
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
            File.WriteAllText(path, json);
        }

        try
        {
            if (File.Exists(path))
            {
                var rules = JsonSerializer.Deserialize<List<TitleRule>>(File.ReadAllText(path));
                if (rules != null)
                    foreach (var r in rules)
                        if (!string.IsNullOrEmpty(r.process) && !string.IsNullOrEmpty(r.title))
                            _rules[r.process] = r.title;
            }
        }
        catch { }
    }

    // Returns the normalized title for a process, or the original title if no rule matches.
    public string Normalize(string processName, string originalTitle)
    {
        if (_rules.TryGetValue(processName, out var canonical))
            return canonical;
        return originalTitle;
    }

    // Returns all rules for the API endpoint.
    public List<TitleRule> GetRules() => _rules.Select(kv => new TitleRule(kv.Key, kv.Value)).ToList();

    public record TitleRule(string process, string title);
}
