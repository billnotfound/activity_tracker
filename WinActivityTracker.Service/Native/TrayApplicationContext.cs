// System tray icon and context menu.
//
// Runs on the WinForms STA thread. Console is an in-app ConsoleWindow form,
// not the system console — closing it does not exit the application.
//
// On first launch, StatusWindow opens after 1s delay.
// "Open Dashboard" / double-click starts the on-demand DashboardServer,
// waits for Kestrel to be ready, then opens the browser.
using System.Diagnostics;
using System.Drawing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon = null!;
    private readonly IServiceProvider _services;
    private readonly DashboardServer _dashboard;

    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "taskmonitor114";

    public static IServiceProvider ServiceProvider { get; set; } = null!;

    public TrayApplicationContext(IServiceProvider services, DashboardServer dashboard, bool autoShowStatus = false)
    {
        _services = services;
        _dashboard = dashboard;
        ServiceProvider = services;

        if (autoShowStatus)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); ShowStatusWindow(); };
            timer.Start();
        }

        var menu = new ContextMenuStrip();

        // --- Dashboard ---
        var dashboardItem = new ToolStripMenuItem(I18nService._("tray.openDashboard"));
        dashboardItem.Click += async (_, _) =>
        {
            await _dashboard.StartAsync();
            OpenUrl($"http://localhost:{_dashboard.Port}");
        };
        dashboardItem.Font = new Font(dashboardItem.Font, FontStyle.Bold);
        menu.Items.Add(dashboardItem);

        // --- Native Settings ---
        var settingsItem = new ToolStripMenuItem(I18nService._("tray.settings"));
        settingsItem.Click += (_, _) => ShowSettingsWindow();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Status window ---
        var statusItem = new ToolStripMenuItem(I18nService._("tray.showStatusWindow"));
        statusItem.Click += (_, _) => ShowStatusWindow();
        menu.Items.Add(statusItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Pause / Resume ---
        var toggleItem = new ToolStripMenuItem(I18nService._("common.pauseTracking"));
        toggleItem.Click += (_, _) =>
        {
            var s = _services.GetRequiredService<SettingsService>();
            s.SetTrackingEnabled(!s.Settings.TrackingEnabled);
            toggleItem.Text = s.Settings.TrackingEnabled ? I18nService._("common.pauseTracking") : I18nService._("common.resumeTracking");
        };
        menu.Items.Add(toggleItem);

        // --- Auto-start ---
        SyncAutoStartOnStartup();

        var settings = _services.GetRequiredService<SettingsService>();
        var autoStartItem = new ToolStripMenuItem(I18nService._("tray.autoStart")) { Checked = settings.Settings.AutoStartEnabled };
        autoStartItem.Click += (_, _) =>
        {
            autoStartItem.Checked = !autoStartItem.Checked;
            ToggleAutoStart(autoStartItem.Checked);
        };
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Console toggle ---
        var consoleItem = new ToolStripMenuItem(I18nService._("tray.showConsole"));
        consoleItem.Click += (_, _) =>
        {
            if (_consoleWindow != null && !_consoleWindow.IsDisposed && _consoleWindow.Visible)
            {
                _consoleWindow.Hide();
                consoleItem.Text = I18nService._("tray.showConsole");
            }
            else
            {
                if (_consoleWindow == null || _consoleWindow.IsDisposed)
                {
                    _consoleWindow = new ConsoleWindow(_services.GetRequiredService<ConsoleMirror>());
                    _consoleWindow.FormClosed += (_, _) => consoleItem.Text = I18nService._("tray.showConsole");
                }
                _consoleWindow.Show();
                _consoleWindow.Activate();
                consoleItem.Text = I18nService._("tray.hideConsole");
            }
        };
        menu.Items.Add(consoleItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Exit ---
        var exitItem = new ToolStripMenuItem(I18nService._("tray.exit"));
        exitItem.Click += (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = IconHelper.GetTimerIcon(),
            Text = "taskmonitor114",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += async (_, _) =>
        {
            await _dashboard.StartAsync();
            OpenUrl($"http://localhost:{_dashboard.Port}");
        };
    }

    // ===== Open URL in browser =====

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(I18nService._("tray.cannotOpenBrowser", ex.Message), "taskmonitor114",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ===== Native windows =====

    private void ShowSettingsWindow()
    {
        if (_settingsWindow != null && !_settingsWindow.IsDisposed)
        {
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        var s = _services.GetRequiredService<SettingsService>();
        _settingsWindow = new SettingsWindow(s);
        _settingsWindow.FormClosed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ShowStatusWindow()
    {
        if (_statusWindow != null && !_statusWindow.IsDisposed)
        {
            _statusWindow.Show();
            _statusWindow.Activate();
            return;
        }

        _statusWindow = new StatusWindow(_services);
        _statusWindow.FormClosed += (_, _) => _statusWindow = null;
        _statusWindow.Show();
    }

    private SettingsWindow? _settingsWindow;
    private StatusWindow? _statusWindow;

    // ===== In-app console =====
    private ConsoleWindow? _consoleWindow;

    // ===== Auto-start (registry ↔ settings.json) =====

    private void SyncAutoStartOnStartup()
    {
        var settings = _services.GetRequiredService<SettingsService>();
        WriteRegistryAutoStart(settings.Settings.AutoStartEnabled);
    }

    private void ToggleAutoStart(bool enable)
    {
        WriteRegistryAutoStart(enable);
        _services.GetRequiredService<SettingsService>().SetAutoStartEnabled(enable);
    }

    public static void WriteRegistryAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, writable: true);
            if (key == null) return;
            if (enable)
                key.SetValue(AutoStartValue, $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\" --autostart");
            else
                key.DeleteValue(AutoStartValue, throwOnMissingValue: false);
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _settingsWindow?.Dispose();
            _statusWindow?.Dispose();
            _consoleWindow?.Dispose();
        }
        base.Dispose(disposing);
    }
}
