// Native settings form — full configuration UI for the backend.
//
// Replaces the browser-based Settings page for users who prefer a native window.
// Reads from and writes to SettingsService (which persists to settings.json).
// All changes take effect immediately on save (trackers read settings each poll cycle).
//
// Auto-start: toggles a registry entry at HKCU\...\Run.
// Requires no admin rights (Current User registry hive).
using Microsoft.Win32;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public partial class SettingsWindow : Form
{
    private readonly SettingsService _settings;
    private readonly int _apiPort;

    // Controls
    private CheckBox _trackingCheck = null!;
    private NumericUpDown _windowInterval = null!;
    private NumericUpDown _processInterval = null!;
    private NumericUpDown _mediaInterval = null!;
    private NumericUpDown _idleThreshold = null!;
    private TextBox _excludedBox = null!;
    private NumericUpDown _retentionDays = null!;
    private CheckBox _autoStartCheck = null!;
    private Button _saveButton = null!;
    private Button _cancelButton = null!;
    private Label _statusLabel = null!;

    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "WinActivityTracker";

    public SettingsWindow(SettingsService settings, int apiPort)
    {
        _settings = settings;
        _apiPort = apiPort;

        Text = "WinActivityTracker — 设置";
        Size = new Size(480, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        BuildUI();
        LoadSettings();
    }

    private void BuildUI()
    {
        var pad = 12;
        var y = pad;
        var lblW = 180;
        var ctrlX = pad + lblW + 8;
        var ctrlW = 240;

        // --- Tracking ---
        var trackGroup = NewGroup("追踪控制", pad, ref y, 52);
        _trackingCheck = new CheckBox
        {
            Text = "启用追踪", Location = new Point(pad, 20), Size = new Size(120, 24),
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold)
        };
        trackGroup.Controls.Add(_trackingCheck);
        Controls.Add(trackGroup);

        // --- Polling ---
        var pollGroup = NewGroup("轮询间隔 (秒)", pad, ref y, 110);
        AddRow(pollGroup, "窗口/焦点轮询:", out _windowInterval, 1, 3600, 3);
        AddRow(pollGroup, "后台进程轮询:", out _processInterval, 5, 3600, 30);
        AddRow(pollGroup, "媒体播放检测:", out _mediaInterval, 1, 3600, 5);
        Controls.Add(pollGroup);

        // --- Idle ---
        var idleGroup = NewGroup("空闲检测", pad, ref y, 52);
        AddRow(idleGroup, "空闲判定阈值 (分钟):", out _idleThreshold, 1, 120, 2);
        Controls.Add(idleGroup);

        // --- Exclusions ---
        var exclGroup = NewGroup("进程排除 (逗号分隔)", pad, ref y, 76);
        _excludedBox = new TextBox
        {
            Location = new Point(pad, 22), Size = new Size(ctrlW + lblW - 4, 23),
            Font = new Font("Consolas", 9)
        };
        var exclHint = new Label
        {
            Text = "不区分大小写。示例: explorer, SearchApp, TextInputHost",
            Location = new Point(pad, 48), Size = new Size(ctrlW + lblW, 16),
            ForeColor = SystemColors.GrayText, Font = new Font("Microsoft YaHei UI", 8)
        };
        exclGroup.Controls.Add(_excludedBox);
        exclGroup.Controls.Add(exclHint);
        Controls.Add(exclGroup);

        // --- Database ---
        var dbGroup = NewGroup("数据库", pad, ref y, 52);
        AddRow(dbGroup, "数据保留天数:", out _retentionDays, 1, 3650, 90);
        Controls.Add(dbGroup);

        // --- Auto-start ---
        var startGroup = NewGroup("系统集成", pad, ref y, 52);
        _autoStartCheck = new CheckBox
        {
            Text = "开机自动启动",
            Location = new Point(pad, 20), Size = new Size(200, 24),
            Font = new Font("Microsoft YaHei UI", 9)
        };
        startGroup.Controls.Add(_autoStartCheck);
        Controls.Add(startGroup);

        // --- Buttons ---
        var btnY = y + 8;
        _saveButton = new Button
        {
            Text = "保存", Location = new Point(pad + lblW - 40, btnY),
            Size = new Size(90, 32), Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold)
        };
        _saveButton.Click += (_, _) => SaveSettings();
        Controls.Add(_saveButton);

        _cancelButton = new Button
        {
            Text = "取消", Location = new Point(pad + lblW + 60, btnY),
            Size = new Size(90, 32)
        };
        _cancelButton.Click += (_, _) => Close();
        Controls.Add(_cancelButton);

        _statusLabel = new Label
        {
            Location = new Point(pad, btnY + 6), Size = new Size(300, 20),
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(_statusLabel);
    }

    private void LoadSettings()
    {
        var s = _settings.Settings;
        _trackingCheck.Checked = s.TrackingEnabled;
        _windowInterval.Value = s.WindowPollSeconds;
        _processInterval.Value = s.ProcessPollSeconds;
        _mediaInterval.Value = s.MediaPollSeconds;
        _idleThreshold.Value = s.IdleThresholdMinutes;
        _excludedBox.Text = string.Join(", ", s.ExcludedProcesses);
        _retentionDays.Value = s.DataRetentionDays;
        _autoStartCheck.Checked = IsAutoStartEnabled();
    }

    private void SaveSettings()
    {
        var s = _settings.Settings;
        s.TrackingEnabled = _trackingCheck.Checked;
        s.WindowPollSeconds = (int)_windowInterval.Value;
        s.ProcessPollSeconds = (int)_processInterval.Value;
        s.MediaPollSeconds = (int)_mediaInterval.Value;
        s.IdleThresholdMinutes = (int)_idleThreshold.Value;
        s.ExcludedProcesses = _excludedBox.Text
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
        s.DataRetentionDays = (int)_retentionDays.Value;

        // SettingsService.Update enforces minimum values and calls Save()
        _settings.Update(s);

        // Auto-start is separate from tracker settings
        SetAutoStart(_autoStartCheck.Checked);

        _statusLabel.Text = "已保存 — 设置即时生效";
        _statusLabel.ForeColor = Color.DarkGreen;
    }

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
        catch { /* Registry access denied — silently skip */ }
    }

    // ===== Layout helpers =====

    private static GroupBox NewGroup(string title, int x, ref int y, int height)
    {
        var g = new GroupBox
        {
            Text = title, Location = new Point(x, y), Size = new Size(440, height)
        };
        y += height + 8;
        return g;
    }

    private static void AddRow(GroupBox parent, string label, out NumericUpDown nud, int min, int max, int defaultVal)
    {
        var lbl = new Label { Text = label, Location = new Point(12, 22 + parent.Controls.Count * 0), Size = new Size(150, 23) };
        nud = new NumericUpDown
        {
            Location = new Point(220, 20 + parent.Controls.Count * 0),
            Size = new Size(80, 23), Minimum = min, Maximum = max, Value = defaultVal
        };
        // Stack rows manually since GroupBox doesn't auto-layout
        var row = parent.Controls.Count / 2;
        lbl.Top = 20 + row * 28;
        nud.Top = 18 + row * 28;
        parent.Controls.Add(lbl);
        parent.Controls.Add(nud);
    }
}
