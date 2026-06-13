// Helper class for loading embedded icon resources
using System.Drawing;

namespace WinActivityTracker.Service.Native;

public static class IconHelper
{
    private static Icon? _timerIcon;
    private static Icon? _settingsIcon;

    public static Icon GetTimerIcon()
    {
        if (_timerIcon != null) return _timerIcon;

        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("timer.ico");
            if (stream != null)
            {
                _timerIcon = new Icon(stream);
                return _timerIcon;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load timer icon: {ex.Message}");
        }

        _timerIcon = SystemIcons.Application;
        return _timerIcon;
    }

    public static Icon GetSettingsIcon()
    {
        if (_settingsIcon != null) return _settingsIcon;

        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("settings.ico");
            if (stream != null)
            {
                _settingsIcon = new Icon(stream);
                return _settingsIcon;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings icon: {ex.Message}");
        }

        _settingsIcon = SystemIcons.Application;
        return _settingsIcon;
    }
}
