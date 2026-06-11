using System.Text.Json;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// Loads i18n JSON bundles from the i18n/ directory. Provides a static
/// shortener I18nService._("key") so WinForms windows and services can
/// use translations without DI constructor changes.
/// </summary>
public class I18nService
{
    private static I18nService? _instance;
    private readonly Dictionary<string, string> _strings;

    public static bool IsAvailable => _instance != null;

    /// <summary>
    /// Returns the translated string for key, or the key itself as fallback.
    /// Before I18nService is initialized (e.g., during early DI construction),
    /// returns the key unchanged.
    /// </summary>
    public static string _(string key)
    {
        if (_instance?._strings.TryGetValue(key, out var val) == true)
            return val;
        return key;
    }

    /// <summary>
    /// Returns the translated and formatted string.
    /// Uses string.Format positional {0}, {1}, etc. placeholders.
    /// If the template doesn't contain {0}, extra args are ignored.
    /// </summary>
    public static string _(string key, params object[] args)
    {
        var template = _(key);
        if (args.Length > 0 && template.Contains("{0}"))
            return string.Format(template, args);
        return template;
    }

    public I18nService(string locale)
    {
        _strings = LoadBundle(locale);
        _instance = this;
    }

    private static Dictionary<string, string> LoadBundle(string locale)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "i18n", $"{locale}.json");

        if (!File.Exists(path))
        {
            // Fallback: try just the language part (e.g., "zh" → "zh-CN")
            var lang = locale.Split('-')[0];
            if (lang != locale)
                path = Path.Combine(baseDir, "i18n", $"{lang}.json");
        }

        if (!File.Exists(path))
            path = Path.Combine(baseDir, "i18n", "en-US.json"); // ultimate fallback

        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
