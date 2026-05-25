// Native settings form using TableLayoutPanel for adaptive layout.
// GroupBoxes have fixed minimum heights and Anchor so they're always visible.
// The outer TableLayoutPanel uses AutoSize rows so everything stacks naturally.
using Microsoft.Win32;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public partial class SettingsWindow : Form
{
    private readonly SettingsService _settings;

    private CheckBox _trackingCheck = null!;
    private NumericUpDown _windowInterval = null!;
    private NumericUpDown _processInterval = null!;
    private NumericUpDown _mediaInterval = null!;
    private NumericUpDown _idleThreshold = null!;
    private TextBox _excludedBox = null!;
    private NumericUpDown _retentionDays = null!;
    private NumericUpDown _apiPortInput = null!;
    private CheckBox _autoStartCheck = null!;
    private Label _statusLabel = null!;

    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "WinActivityTracker";

    public SettingsWindow(SettingsService settings, int apiPort)
    {
        _settings = settings;

        Text = "WinActivityTracker — 设置";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(440, 860);
        Size = new Size(680, 900);
        ShowInTaskbar = true;
        AutoScroll = true;
        Padding = new Padding(12);

        BuildUI();
        LoadSettings();
    }

    private void BuildUI()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
        };

        // --- Tracking ---
        _trackingCheck = new CheckBox
        {
            Text = "启用追踪", AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
            Anchor = AnchorStyles.Left
        };
        table.Controls.Add(_trackingCheck);

        // --- Helper: Add a group with an inner 2-column table ---
        TableLayoutPanel AddGroupBox(string title, int rows, Action<TableLayoutPanel> fill)
        {
            var g = new GroupBox
            {
                Text = title,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Margin = new Padding(0, 8, 0, 0),
                MinimumSize = new Size(0, rows * 34 + 42)
            };
            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(8, 4, 8, 4),
                ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100) }
            };
            fill(inner);
            g.Controls.Add(inner);
            table.Controls.Add(g);
            return inner;
        }

        void AddRow(TableLayoutPanel parent, string label, out NumericUpDown nud, int min, int max, int def)
        {
            parent.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 12, 4) });
            nud = new NumericUpDown { Minimum = min, Maximum = max, Value = def, Width = 80, Anchor = AnchorStyles.Left };
            parent.Controls.Add(nud);
        }

        // --- Polling ---
        AddGroupBox("轮询间隔 (秒)", 3, inner =>
        {
            AddRow(inner, "窗口/焦点轮询:", out _windowInterval, 1, 3600, 3);
            AddRow(inner, "后台进程轮询:", out _processInterval, 5, 3600, 30);
            AddRow(inner, "媒体播放检测:", out _mediaInterval, 1, 3600, 5);
        });

        // --- Idle ---
        AddGroupBox("空闲检测", 1, inner =>
        {
            AddRow(inner, "空闲判定阈值 (分钟):", out _idleThreshold, 1, 120, 2);
        });

        // --- Exclusions ---
        var excl = AddGroupBox("进程排除 (逗号分隔，不区分大小写)", 1, inner =>
        {
            inner.ColumnStyles.Clear();
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _excludedBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Consolas", 9), Margin = new Padding(0, 4, 0, 4) };
            inner.Controls.Add(_excludedBox);
        });

        // --- Database ---
        AddGroupBox("数据库", 1, inner =>
        {
            AddRow(inner, "数据保留天数:", out _retentionDays, 1, 3650, 90);
        });

        // --- Server ---
        AddGroupBox("服务器 (重启后生效)", 1, inner =>
        {
            AddRow(inner, "API 端口:", out _apiPortInput, 1024, 65535, 5200);
        });

        // --- Auto-start ---
        _autoStartCheck = new CheckBox
        {
            Text = "开机自动启动", AutoSize = true,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0)
        };
        table.Controls.Add(_autoStartCheck);

        // --- Buttons ---
        var btnPanel = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 12, 0, 0)
        };
        var saveBtn = new Button { Text = "保存", Size = new Size(100, 36), Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold) };
        saveBtn.Click += (_, _) => SaveSettings();
        var cancelBtn = new Button { Text = "取消", Size = new Size(100, 36), Font = new Font("Microsoft YaHei UI", 10) };
        cancelBtn.Click += (_, _) => Close();
        _statusLabel = new Label { Text = "", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 0, 0) };
        btnPanel.Controls.Add(saveBtn);
        btnPanel.Controls.Add(cancelBtn);
        btnPanel.Controls.Add(_statusLabel);
        table.Controls.Add(btnPanel);

        Controls.Add(table);
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
        _apiPortInput.Value = s.ApiPort;
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
        s.ExcludedProcesses = _excludedBox.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        s.DataRetentionDays = (int)_retentionDays.Value;
        s.ApiPort = (int)_apiPortInput.Value;
        _settings.Update(s);
        SetAutoStart(_autoStartCheck.Checked);
        _statusLabel.Text = "已保存";
        _statusLabel.ForeColor = Color.DarkGreen;
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey);
            return key?.GetValue(AutoStartValue) is string path && File.Exists(path.Trim('"'));
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, writable: true);
            if (key == null) return;
            if (enable) key.SetValue(AutoStartValue, $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"");
            else key.DeleteValue(AutoStartValue, throwOnMissingValue: false);
        }
        catch { }
    }
}
