// Live status window — current focus, Top 5, tracking controls.
// Uses TableLayoutPanel with Anchor — no hardcoded Y positions.
using System.Net.Http.Json;

namespace WinActivityTracker.Service.Native;

public partial class StatusWindow : Form
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly string _apiBase;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private Label _focusLabel = null!;
    private ListView _topList = null!;
    private Label _statusLabel = null!;
    private Label _offLabel = null!;
    private Button _toggleButton = null!;

    public StatusWindow(IServiceProvider services, int apiPort)
    {
        _apiBase = $"http://localhost:{apiPort}";

        Text = "状态";
        Size = new Size(560, 680);
        MinimumSize = new Size(440, 440);
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(12);

        BuildUI(services);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += async (_, _) => await RefreshData(services);
        _refreshTimer.Start();

        _ = RefreshData(services);
    }

    private void BuildUI(IServiceProvider services)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) },
            RowStyles = {
                new RowStyle(SizeType.AutoSize),   // focus
                new RowStyle(SizeType.Percent, 100), // top5 — takes all remaining space
                new RowStyle(SizeType.AutoSize)    // controls
            }
        };

        // --- Focus group ---
        {
            var g = new GroupBox
            {
                Text = "当前焦点窗口", Height = 76,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Margin = new Padding(0, 0, 0, 8)
            };
            _focusLabel = new Label
            {
                Text = "加载中...", Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold),
                Dock = DockStyle.Fill
            };
            g.Controls.Add(_focusLabel);
            table.Controls.Add(g);
        }

        // --- Top 5 list (fills remaining space) ---
        {
            var g = new GroupBox
            {
                Text = "今日焦点时长 Top 5",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            _topList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Microsoft YaHei UI", 10)
            };
            _topList.Columns.Add("程序", 200);
            _topList.Columns.Add("时长", 120);
            _topList.Columns.Add("切换", 80);
            g.Controls.Add(_topList);
            table.Controls.Add(g);
        }

        // --- Controls ---
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 0)
            };

            _offLabel = new Label
            {
                Text = "", AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9),
                ForeColor = Color.Gray,
                Margin = new Padding(0, 4, 16, 0)
            };
            panel.Controls.Add(_offLabel);

            _statusLabel = new Label
            {
                Text = "追踪状态: 检测中...", AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10),
                Margin = new Padding(0, 4, 16, 0)
            };
            panel.Controls.Add(_statusLabel);

            _toggleButton = new Button
            {
                Text = "暂停追踪", Size = new Size(120, 38),
                Font = new Font("Microsoft YaHei UI", 9)
            };
            _toggleButton.Click += async (_, _) => await ToggleTracking(services);
            panel.Controls.Add(_toggleButton);

            var topMostCheck = new CheckBox
            {
                Text = "窗口置顶", Checked = true, AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9),
                Margin = new Padding(12, 6, 0, 0)
            };
            topMostCheck.CheckedChanged += (_, _) => TopMost = topMostCheck.Checked;
            TopMost = true;
            panel.Controls.Add(topMostCheck);

            table.Controls.Add(panel);
        }

        Controls.Add(table);
    }

    private async Task RefreshData(IServiceProvider services)
    {
        try
        {
            var winResp = await _http.GetAsync($"{_apiBase}/api/windows/current");
            if (winResp.IsSuccessStatusCode)
            {
                var windows = await winResp.Content.ReadFromJsonAsync<List<WindowInfo>>();
                var focused = windows?.FirstOrDefault(w => w.IsFocused);
                _focusLabel.Text = focused != null
                    ? $"{focused.ProcessName} — {Truncate(focused.Title, 60)}"
                    : "无焦点窗口";
            }

            var sumResp = await _http.GetAsync($"{_apiBase}/api/summary/today");
            if (sumResp.IsSuccessStatusCode)
            {
                var wrapped = await sumResp.Content.ReadFromJsonAsync<SummaryResponse>();
                var summary = wrapped?.Items ?? [];
                var totalOff = wrapped?.TotalSleepSeconds ?? 0;
                _offLabel.Text = totalOff > 0 ? $"今日休眠/关机: {FmtDur(totalOff)}" : "";
                var top5 = summary.Take(5).ToList();

                // Load adjusted switch counts if merge is enabled
                var svc = services.GetRequiredService<WinActivityTracker.Core.Services.SettingsService>();
                var adjustedSwitches = new Dictionary<string, int>();
                if (svc.Settings.MergeSameProcessSwitches)
                {
                    var tlResp = await _http.GetAsync($"{_apiBase}/api/windows/timeline?from={DateTime.Now:yyyy-MM-dd}T00:00:00&to={DateTime.Now:yyyy-MM-dd}T23:59:59");
                    if (tlResp.IsSuccessStatusCode)
                    {
                        var tlWrapped = await tlResp.Content.ReadFromJsonAsync<TimelineResponse>();
                        var timeline = tlWrapped?.Data;
                        if (timeline != null)
                        {
                            string prev = "";
                            foreach (var t in timeline)
                            {
                                if (t.ProcessName != prev) adjustedSwitches[t.ProcessName] = adjustedSwitches.GetValueOrDefault(t.ProcessName) + 1;
                                prev = t.ProcessName;
                            }
                        }
                    }
                }

                _topList.BeginUpdate();
                _topList.Items.Clear();
                foreach (var item in top5)
                {
                    var lvi = new ListViewItem(item.ProcessName);
                    lvi.SubItems.Add(FmtDur(item.TotalSeconds));
                    var sc = adjustedSwitches.GetValueOrDefault(item.ProcessName, item.SwitchCount);
                    lvi.SubItems.Add(sc.ToString());
                    _topList.Items.Add(lvi);
                }
                _topList.EndUpdate();
            }

            var settings = services.GetRequiredService<WinActivityTracker.Core.Services.SettingsService>();
            var enabled = settings.Settings.TrackingEnabled;
            _statusLabel.Text = enabled ? "● 追踪运行中" : "○ 追踪已暂停";
            _statusLabel.ForeColor = enabled ? Color.DarkGreen : Color.DarkOrange;
            _toggleButton.Text = enabled ? "暂停追踪" : "恢复追踪";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StatusWindow refresh error: {ex.Message}");
            _statusLabel.Text = "⚠ 无响应";
            _statusLabel.ForeColor = Color.Red;
        }
    }

    private async Task ToggleTracking(IServiceProvider services)
    {
        var settings = services.GetRequiredService<WinActivityTracker.Core.Services.SettingsService>();
        settings.Settings.TrackingEnabled = !settings.Settings.TrackingEnabled;
        settings.Save();
        try
        {
            await _http.PutAsJsonAsync($"{_apiBase}/api/settings", settings.Settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ToggleTracking error: {ex.Message}");
        }
        await RefreshData(services);
    }

    private static string FmtDur(double s)
    {
        if (s < 60) return $"{s:F0}秒";
        if (s < 3600) return $"{s / 60:F1}分";
        return $"{s / 3600:F1}时";
    }

    private static string Truncate(string text, int n)
        => text.Length <= n ? text : text[..(n - 3)] + "...";

    private record WindowInfo(string ProcessName, string Title, bool IsFocused);
    private record SummaryResponse(List<SummaryItem> Items, double TotalSleepSeconds);
    private record SummaryItem(string ProcessName, double TotalSeconds, int SwitchCount);
    private record TimelineItem(string ProcessName, double DurationSeconds);
    private record TimelineResponse(List<TimelineItem> Data, long Total, int Offset, int Limit);
}
