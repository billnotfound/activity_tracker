// Manages the system tray icon and its context menu.
//
// Runs on the WinForms STA thread via Application.Run().
// OutputType is WinExe — no console window on startup.
// Console is an in-app WinForms window (ConsoleWindow), not the system console —
// closing it never exits the application.
//
// Menu structure:
//   打开仪表盘        → browser opens SPA on localhost:apiPort
//   设置...           → native SettingsWindow
//   显示状态窗口       → native StatusWindow
//   ─────────────────
//   暂停/恢复追踪      → toggle trackingEnabled
//   开机自启           → toggle HKCU\...\Run registry entry
//   显示/隐藏控制台     → show/hide ConsoleWindow (ConsoleMirror subscriber)
//   ─────────────────
//   退出              → stop web host + close tray
//
// Double-clicking the tray icon opens the dashboard.
// On first launch, the StatusWindow opens automatically after a 1-second delay.
using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon = null!;
    private readonly IServiceProvider _services;
    private readonly int _apiPort;

    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "taskmonitor114";

    // Set by Program.cs before Application.Run() so native windows can access DI.
    public static IServiceProvider ServiceProvider { get; set; } = null!;

    // autoShowStatus: when true, opens the StatusWindow shortly after startup
    // (first-launch behavior — user clicked the exe and expects to see something).
    public TrayApplicationContext(IServiceProvider services, int apiPort, bool autoShowStatus = false)
    {
        _services = services;
        _apiPort = apiPort;
        ServiceProvider = services;

        if (autoShowStatus)
        {
            // Delay to let the web host fully start before the StatusWindow queries the API
            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); ShowStatusWindow(); };
            timer.Start();
        }

        var menu = new ContextMenuStrip();

        // --- Dashboard ---
        var dashboardItem = new ToolStripMenuItem("打开仪表盘");
        dashboardItem.Click += (_, _) => OpenUrl($"http://localhost:{_apiPort}");
        dashboardItem.Font = new Font(dashboardItem.Font, FontStyle.Bold);
        menu.Items.Add(dashboardItem);

        // --- Native Settings ---
        var settingsItem = new ToolStripMenuItem("设置...");
        settingsItem.Click += (_, _) => ShowSettingsWindow();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Status window ---
        var statusItem = new ToolStripMenuItem("显示状态窗口");
        statusItem.Click += (_, _) => ShowStatusWindow();
        menu.Items.Add(statusItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Pause / Resume ---
        var toggleItem = new ToolStripMenuItem("暂停追踪");
        toggleItem.Click += (_, _) =>
        {
            var s = _services.GetRequiredService<SettingsService>();
            s.Settings.TrackingEnabled = !s.Settings.TrackingEnabled;
            s.Save();
            toggleItem.Text = s.Settings.TrackingEnabled ? "暂停追踪" : "恢复追踪";
        };
        menu.Items.Add(toggleItem);

        // --- Auto-start ---
        // Sync registry → settings.json on startup so settings.json is the truth.
        SyncAutoStartOnStartup();

        var settings = _services.GetRequiredService<SettingsService>();
        var autoStartItem = new ToolStripMenuItem("开机自启") { Checked = settings.Settings.AutoStartEnabled };
        autoStartItem.Click += (_, _) =>
        {
            autoStartItem.Checked = !autoStartItem.Checked;
            ToggleAutoStart(autoStartItem.Checked);
        };
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Console toggle (in-app window, not system console) ---
        var consoleItem = new ToolStripMenuItem("显示控制台");
        consoleItem.Click += (_, _) =>
        {
            if (_consoleWindow != null && !_consoleWindow.IsDisposed && _consoleWindow.Visible)
            {
                _consoleWindow.Hide();
                consoleItem.Text = "显示控制台";
            }
            else
            {
                if (_consoleWindow == null || _consoleWindow.IsDisposed)
                {
                    _consoleWindow = new ConsoleWindow(_apiPort, _services.GetRequiredService<ConsoleMirror>());
                    _consoleWindow.FormClosed += (_, _) => consoleItem.Text = "显示控制台";
                }
                _consoleWindow.Show();
                _consoleWindow.Activate();
                consoleItem.Text = "隐藏控制台";
            }
        };
        menu.Items.Add(consoleItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Exit ---
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "taskmonitor114",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => OpenUrl($"http://localhost:{_apiPort}");
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
            MessageBox.Show($"无法打开浏览器: {ex.Message}", "taskmonitor114",
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
        _settingsWindow = new SettingsWindow(s, _apiPort);
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

        _statusWindow = new StatusWindow(_services, _apiPort);
        _statusWindow.FormClosed += (_, _) => _statusWindow = null;
        _statusWindow.Show();
    }

    private SettingsWindow? _settingsWindow;
    private StatusWindow? _statusWindow;

    // ===== In-app console (WinForms window, not the system console) =====
    // Avoids AllocConsole issues: encoding, Ctrl handler conflicts with .NET runtime,
    // and process termination when the user closes the console window.
    // Console.Out is redirected to the ConsoleWindow's TextBox on first show.
    private ConsoleWindow? _consoleWindow;

    // ===== Auto-start (registry ↔ settings.json) =====

    // On startup: read registry, write to settings.json as the single source of truth.
    private void SyncAutoStartOnStartup()
    {
        var settings = _services.GetRequiredService<SettingsService>();
        bool inRegistry;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey);
            inRegistry = key?.GetValue(AutoStartValue) is string path && File.Exists(path.Trim('"'));
        }
        catch { inRegistry = false; }
        settings.Settings.AutoStartEnabled = inRegistry;
        settings.Save();
    }

    // Toggles both registry and settings.json. Called by tray menu checkbox.
    private void ToggleAutoStart(bool enable)
    {
        WriteRegistryAutoStart(enable);
        var settings = _services.GetRequiredService<SettingsService>();
        settings.Settings.AutoStartEnabled = enable;
        settings.Save();
    }

    // Writes/deletes the registry Run key. Also called by SettingsWindow.
    public static void WriteRegistryAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, writable: true);
            if (key == null) return;
            if (enable)
                key.SetValue(AutoStartValue, $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"");
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
        }
        base.Dispose(disposing);
    }
}
