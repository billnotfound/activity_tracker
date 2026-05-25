// Manages the system tray icon and its context menu.
//
// Runs on the WinForms STA thread via Application.Run().
// Receives the IServiceProvider from Program.cs to access SettingsService and API data.
//
// OutputType is WinExe — no console window on startup.
// Console can be shown/hidden via the tray menu using AllocConsole/FreeConsole.
// When the user closes the console window (X button), only the console is freed —
// the tray and all background services continue running.
//
// Menu structure:
//   打开仪表盘        → browser opens Vue SPA on localhost:apiPort
//   设置...           → native SettingsWindow
//   显示状态窗口       → native StatusWindow (current focus + top 5)
//   ─────────────────
//   暂停/恢复追踪      → toggle trackingEnabled
//   开机自启           → toggle HKCU\...\Run registry entry
//   显示/隐藏控制台     → AllocConsole / FreeConsole
//   ─────────────────
//   退出              → stop web host + close tray
//
// Double-clicking the tray icon opens the dashboard.
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private const string AutoStartValue = "WinActivityTracker";

    // Set by Program.cs before Application.Run() so native windows can access DI.
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
        var autoStartItem = new ToolStripMenuItem("开机自启")
        {
            Checked = IsAutoStartEnabled(),
            CheckOnClick = true
        };
        autoStartItem.Click += (_, _) => SetAutoStart(autoStartItem.Checked);
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- Console toggle ---
        var consoleItem = new ToolStripMenuItem("显示控制台");
        consoleItem.Click += (_, _) =>
        {
            if (_consoleVisible)
            {
                FreeConsole();
                _consoleVisible = false;
                consoleItem.Text = "显示控制台";
            }
            else
            {
                AllocConsole();
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                Console.WriteLine("=== WinActivityTracker Console ===");
                Console.WriteLine($"API: http://localhost:{_apiPort}");
                Console.WriteLine("关闭此窗口不会退出程序。");
                Console.WriteLine("===================================");
                _consoleVisible = true;
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
            Text = "WinActivityTracker",
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
            MessageBox.Show($"无法打开浏览器: {ex.Message}", "WinActivityTracker",
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

    // ===== Console management =====
    // WinExe output type means no console on startup.
    // AllocConsole creates one; FreeConsole detaches it.
    // Closing the console window (X button) also calls FreeConsole internally;
    // we detect this on the next menu click (operations on freed console fail silently).

    private bool _consoleVisible;

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    // ===== Auto-start via Registry =====

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey);
            return key?.GetValue(AutoStartValue) is string path &&
                   File.Exists(path.Trim('"'));
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                key.SetValue(AutoStartValue, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AutoStartValue, throwOnMissingValue: false);
            }
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
