// Loads title_rules.json from ConfigDir (AppPaths).
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

    public TitleNormalizer(AppPaths? appPaths = null)
    {
        var dir = AppPaths.ResolveConfigDir(appPaths);
        _filePath = Path.Combine(dir, "title_rules.json");

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
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
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

    private static object[] ZhDefaults() => new object[]
    {
        new { _comment = "=== 窗口标题归一化规则 ===" },
        new { _comment = "用于将浏览器等程序的长标题（如 \"GitHub - Firefox\"）归一化为简短固定名称。" },
        new { _comment = "" },
        new { _comment = "两种规则类型：" },
        new { _comment = "  1. 固定替换：设置 title 字段，窗口标题直接替换为固定字符串。" },
        new { _comment = "  2. 正则替换：设置 titleRegex + titleReplacement，对原标题做正则替换。" },
        new { _comment = "     如 fish 的标题 \"~/projects fish\" 经正则 ^(\\S+) 替换后得到 \"~/projects\"。" },
        new { _comment = "  同时指定时，固定替换（title）优先。" },
        new { _comment = "" },
        new { _comment = "字段说明：" },
        new { _comment = "  process: 进程名（大小写不敏感），如 firefox、msedge" },
        new { _comment = "  title: 固定替换后的标题（可选）" },
        new { _comment = "  titleRegex: 正则匹配模式（可选）" },
        new { _comment = "  titleReplacement: 正则替换内容（可选）" },
        new { _comment = "" },
        new { _comment = "=== 浏览器 ===" },
        new TitleRule { Process = "firefox",            Title = "Mozilla Firefox" },
        new TitleRule { Process = "msedge",             Title = "Microsoft Edge" },
        new TitleRule { Process = "chrome",             Title = "Google Chrome" },
        new { _comment = "" },
        new { _comment = "=== 终端 ===" },
        new TitleRule { Process = "WindowsTerminal",    Title = "Windows Terminal" },
        new TitleRule { Process = "fish", TitleRegex = @"^(\S+)", TitleReplacement = "$1" },
        new TitleRule { Process = "bash", TitleRegex = @"^(\S+)", TitleReplacement = "$1" },
        new { _comment = "" },
        new { _comment = "=== 系统 ===" },
        new TitleRule { Process = "ApplicationFrameHost", Title = "[UWP App]" },
    };

    private static object[] EnDefaults() => new object[]
    {
        new { _comment = "=== Window Title Normalization Rules ===" },
        new { _comment = "Collapses long browser/program titles (e.g. \"GitHub - Firefox\") into short canonical names." },
        new { _comment = "" },
        new { _comment = "Two rule types:" },
        new { _comment = "  1. Fixed replacement: set the title field — window title is replaced with a fixed string." },
        new { _comment = "  2. Regex substitution: set titleRegex + titleReplacement — regex replace on original title." },
        new { _comment = "     e.g. fish title \"~/projects\" matched by ^(\\S+) becomes \"~/projects\"." },
        new { _comment = "  When both are set, fixed replacement (title) takes priority." },
        new { _comment = "" },
        new { _comment = "Fields:" },
        new { _comment = "  process: process name (case-insensitive), e.g. firefox, msedge" },
        new { _comment = "  title: fixed replacement title (optional)" },
        new { _comment = "  titleRegex: regex match pattern (optional)" },
        new { _comment = "  titleReplacement: regex replacement string (optional)" },
        new { _comment = "" },
        new { _comment = "=== Browsers ===" },
        new TitleRule { Process = "firefox",            Title = "Mozilla Firefox" },
        new TitleRule { Process = "msedge",             Title = "Microsoft Edge" },
        new TitleRule { Process = "chrome",             Title = "Google Chrome" },
        new TitleRule { Process = "brave",              Title = "Brave Browser" },
        new TitleRule { Process = "opera",              Title = "Opera Browser" },
        new { _comment = "" },
        new { _comment = "=== Terminals ===" },
        new TitleRule { Process = "WindowsTerminal",    Title = "Windows Terminal" },
        new TitleRule { Process = "wezterm-gui",        Title = "WezTerm" },
        new TitleRule { Process = "alacritty",          Title = "Alacritty" },
        new TitleRule { Process = "fish", TitleRegex = @"^(\S+)", TitleReplacement = "$1" },
        new TitleRule { Process = "bash", TitleRegex = @"^(\S+)", TitleReplacement = "$1" },
        new TitleRule { Process = "zsh",  TitleRegex = @"^(\S+)", TitleReplacement = "$1" },
        new { _comment = "" },
        new { _comment = "=== System ===" },
        new TitleRule { Process = "ApplicationFrameHost", Title = "[UWP App]" },
    };
}
