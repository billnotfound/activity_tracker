// Lightweight native status window showing live tracking state.
//
// Features:
//   - Current foreground window (process + title), refreshed every 3s
//   - Today's Top 5 focus duration, refreshed every 10s
//   - Tracking enabled/disabled indicator
//   - Quick toggle button for pause/resume
//
// Data is fetched via the local REST API (localhost) rather than direct DB access.
// This keeps the window decoupled from EF Core scoping and ensures it reflects
// what any API consumer would see.
//
// The window is non-modal — it can stay open alongside other applications.
// Closing it does NOT exit the application (only the tray Exit button does that).
using System.Net.Http.Json;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public partial class StatusWindow : Form
{
    private readonly IServiceProvider _services;
    private readonly string _apiBase;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private Label _statusLabel = null!;
    private Label _focusLabel = null!;
    private ListView _topList = null!;
    private Button _toggleButton = null!;
    private CheckBox _topMostCheck = null!;

    public StatusWindow(IServiceProvider services, int apiPort)
    {
        _services = services;
        _apiBase = $"http://localhost:{apiPort}";

        Text = "WinActivityTracker — 状态";
        Size = new Size(420, 440);
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;

        BuildUI();

        // Timer for live refresh — fires on the UI thread, safe to update controls
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += async (_, _) => await RefreshData();
        _refreshTimer.Start();

        _ = RefreshData();
    }

    private void BuildUI()
    {
        var pad = 12;
        var y = pad;

        // --- Focus window ---
        var focusGroup = new GroupBox
        {
            Text = "当前焦点窗口", Location = new Point(pad, y), Size = new Size(380, 60)
        };
        _focusLabel = new Label
        {
            Location = new Point(pad, 20), Size = new Size(350, 30),
            Text = "加载中...", Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold)
        };
        focusGroup.Controls.Add(_focusLabel);
        Controls.Add(focusGroup);
        y += 70;

        // --- Top 5 ---
        var topGroup = new GroupBox
        {
            Text = "今日焦点时长 Top 5", Location = new Point(pad, y), Size = new Size(380, 180)
        };
        _topList = new ListView
        {
            Location = new Point(pad, 20), Size = new Size(350, 145),
            View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Font = new Font("Microsoft YaHei UI", 9)
        };
        _topList.Columns.Add("程序", 160);
        _topList.Columns.Add("时长", 80);
        _topList.Columns.Add("切换", 60);
        topGroup.Controls.Add(_topList);
        Controls.Add(topGroup);
        y += 190;

        // --- Status + toggle ---
        var ctrlPanel = new Panel { Location = new Point(pad, y), Size = new Size(380, 80) };

        _statusLabel = new Label
        {
            Location = new Point(0, 5), Size = new Size(200, 20),
            Text = "追踪状态: 检测中...", Font = new Font("Microsoft YaHei UI", 9)
        };
        ctrlPanel.Controls.Add(_statusLabel);

        _toggleButton = new Button
        {
            Location = new Point(0, 30), Size = new Size(100, 28),
            Text = "暂停追踪", FlatStyle = FlatStyle.System
        };
        _toggleButton.Click += async (_, _) => await ToggleTracking();
        ctrlPanel.Controls.Add(_toggleButton);

        _topMostCheck = new CheckBox
        {
            Location = new Point(120, 34), Size = new Size(100, 20),
            Text = "窗口置顶", Checked = true
        };
        _topMostCheck.CheckedChanged += (_, _) => TopMost = _topMostCheck.Checked;
        TopMost = true;
        ctrlPanel.Controls.Add(_topMostCheck);

        Controls.Add(ctrlPanel);
    }

    private async Task RefreshData()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            // Fetch current focus window (live, not from DB)
            var winResp = await http.GetAsync($"{_apiBase}/api/windows/current");
            if (winResp.IsSuccessStatusCode)
            {
                var windows = await winResp.Content.ReadFromJsonAsync<List<WindowInfo>>();
                var focused = windows?.FirstOrDefault(w => w.IsFocused);
                if (focused != null)
                    _focusLabel.Text = $"{focused.ProcessName} — {Truncate(focused.Title, 50)}";
            }

            // Fetch today's summary
            var sumResp = await http.GetAsync($"{_apiBase}/api/summary/today");
            if (sumResp.IsSuccessStatusCode)
            {
                var summary = await sumResp.Content.ReadFromJsonAsync<List<SummaryItem>>();
                var top5 = summary?.Take(5).ToList() ?? [];

                _topList.BeginUpdate();
                _topList.Items.Clear();
                foreach (var item in top5)
                {
                    var lvi = new ListViewItem(item.ProcessName);
                    lvi.SubItems.Add(FormatDuration(item.TotalSeconds));
                    lvi.SubItems.Add(item.SwitchCount.ToString());
                    _topList.Items.Add(lvi);
                }
                _topList.EndUpdate();
            }

            // Fetch tracking state
            var settings = _services.GetRequiredService<SettingsService>();
            var enabled = settings.Settings.TrackingEnabled;
            _statusLabel.Text = enabled ? "追踪状态: ● 运行中" : "追踪状态: ○ 已暂停";
            _statusLabel.ForeColor = enabled ? Color.DarkGreen : Color.DarkOrange;
            _toggleButton.Text = enabled ? "暂停追踪" : "恢复追踪";
        }
        catch
        {
            _statusLabel.Text = "追踪状态: ⚠ 无响应";
            _statusLabel.ForeColor = Color.Red;
        }
    }

    private async Task ToggleTracking()
    {
        var settings = _services.GetRequiredService<SettingsService>();
        settings.Settings.TrackingEnabled = !settings.Settings.TrackingEnabled;
        settings.Save();

        // Also broadcast via the API so any external consumers see the update
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            await http.PutAsJsonAsync($"{_apiBase}/api/settings", settings.Settings);
        }
        catch { /* API call is opportunistic — settings file is already saved */ }

        await RefreshData();
    }

    private static string FormatDuration(double s)
    {
        if (s < 60) return $"{s:F0}秒";
        if (s < 3600) return $"{s / 60:F1}分";
        return $"{s / 3600:F1}时";
    }

    private static string Truncate(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";

    // DTOs for System.Text.Json deserialization from local API
    private record WindowInfo(string ProcessName, string Title, bool IsFocused);
    private record SummaryItem(string ProcessName, double TotalSeconds, int SwitchCount);
}
