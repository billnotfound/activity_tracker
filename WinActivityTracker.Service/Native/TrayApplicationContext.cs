// Manages the system tray icon and its context menu.
//
// Runs on the WinForms STA thread via Application.Run().
// Receives the IServiceProvider from Program.cs to access SettingsService and API data.
//
// Menu items open URLs in the default browser rather than embedding a full browser control,
// keeping the native UI lightweight while the Vue SPA handles the full feature set.
//
// Double-clicking the tray icon opens the dashboard (same as the menu item).
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon = null!;  // Assigned in constructor before use
    private readonly IServiceProvider _services;
    private readonly int _apiPort;

    // Set by Program.cs before Application.Run() so StatusWindow can access DI.
    // A static field avoids passing IServiceProvider through ApplicationContext constructor
    // which would require reflection or a custom Application.Run overload.
    public static IServiceProvider ServiceProvider { get; set; } = null!;

    public TrayApplicationContext(IServiceProvider services, int apiPort)
    {
        _services = services;
        _apiPort = apiPort;
        ServiceProvider = services;

        var menu = new ContextMenuStrip();

        // --- Dashboard ---
        var dashboardItem = new ToolStripMenuItem("打开仪表盘");
        dashboardItem.Click += (_, _) => OpenUrl($"http://localhost:{_apiPort}");
        dashboardItem.Font = new Font(dashboardItem.Font, FontStyle.Bold);
        menu.Items.Add(dashboardItem);

        // --- Settings ---
        var settingsItem = new ToolStripMenuItem("打开设置");
        settingsItem.Click += (_, _) => OpenUrl($"http://localhost:{_apiPort}/settings");
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Status window ---
        var statusItem = new ToolStripMenuItem("显示状态窗口");
        statusItem.Click += (_, _) => ShowStatusWindow();
        menu.Items.Add(statusItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Pause / Resume toggle ---
        var toggleItem = new ToolStripMenuItem("暂停追踪");
        toggleItem.Click += (_, _) =>
        {
            var settings = _services.GetRequiredService<SettingsService>();
            settings.Settings.TrackingEnabled = !settings.Settings.TrackingEnabled;
            settings.Save();
            toggleItem.Text = settings.Settings.TrackingEnabled ? "暂停追踪" : "恢复追踪";
        };
        menu.Items.Add(toggleItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Exit ---
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        // Use a built-in icon — no external .ico file needed.
        // SystemIcons.Application is a generic application window icon.
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "WinActivityTracker",
            ContextMenuStrip = menu,
            Visible = true
        };

        // Double-click → open dashboard
        _trayIcon.DoubleClick += (_, _) => OpenUrl($"http://localhost:{_apiPort}");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            // UseShellExecute opens the default browser, even on systems where
            // the HTTP protocol association is non-standard.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开浏览器: {ex.Message}", "WinActivityTracker",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowStatusWindow()
    {
        // Prevent multiple status windows. Show() brings the existing one to front.
        if (_statusWindow != null && !_statusWindow.IsDisposed)
        {
            _statusWindow.Show();
            _statusWindow.Activate();
            return;
        }

        _statusWindow = new StatusWindow(_services, _apiPort);
        _statusWindow.FormClosed += (_, _) => _statusWindow = null;
        _statusWindow.Show();
    }

    private StatusWindow? _statusWindow;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
