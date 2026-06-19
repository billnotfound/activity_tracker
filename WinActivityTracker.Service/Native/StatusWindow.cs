// Live status window — current focus, Top 5, tracking controls.
// All data accessed directly via DI / EF Core — no HTTP dependency on Kestrel.
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Service.Native;

public partial class StatusWindow : Form
{
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private Label _focusLabel = null!;
    private ListView _topList = null!;
    private Label _statusLabel = null!;
    private Label _offLabel = null!;
    private Button _toggleButton = null!;

    public StatusWindow(IServiceProvider services)
    {
        Text = I18nService._("statusWindow.title");
        Size = new Size(560, 680);
        MinimumSize = new Size(440, 440);
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(12);
        Icon = IconHelper.GetTimerIcon();

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
                Text = I18nService._("statusWindow.currentFocus"), Height = 76,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Margin = new Padding(0, 0, 0, 8)
            };
            _focusLabel = new Label
            {
                Text = I18nService._("common.loading"), Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold),
                Dock = DockStyle.Fill
            };
            g.Controls.Add(_focusLabel);
            table.Controls.Add(g);
        }

        // --- Top 5 list (fills remaining space) ---
        {
            var g = new GroupBox
            {
                Text = I18nService._("statusWindow.top5"),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            _topList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Microsoft YaHei UI", 10)
            };
            _topList.Columns.Add(I18nService._("common.process"), 200);
            _topList.Columns.Add(I18nService._("common.duration"), 120);
            _topList.Columns.Add(I18nService._("common.switches"), 80);
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
                Text = I18nService._("statusWindow.trackingStatus"), AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10),
                Margin = new Padding(0, 4, 16, 0)
            };
            panel.Controls.Add(_statusLabel);

            _toggleButton = new Button
            {
                Text = I18nService._("common.pauseTracking"), Size = new Size(120, 38),
                Font = new Font("Microsoft YaHei UI", 9)
            };
            _toggleButton.Click += (_, _) => ToggleTracking(services);
            panel.Controls.Add(_toggleButton);

            var topMostCheck = new CheckBox
            {
                Text = I18nService._("statusWindow.alwaysOnTop"), Checked = true, AutoSize = true,
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
            var processCache = services.GetRequiredService<ProcessNameCache>();

            // 1. Current focus — direct P/Invoke, no API needed
            var windows = WindowTracker.EnumerateVisibleWindows(processCache);
            var focused = windows.FirstOrDefault(w => w.IsFocused);
            _focusLabel.Text = focused != default
                ? $"{focused.ProcessName} — {Truncate(focused.Title, 60)}"
                : I18nService._("statusWindow.noFocusWindow");

            // 2. Today summary — direct EF Core query
            using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = services.GetRequiredService<SettingsService>();

            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);

            // Off periods (sleep/shutdown)
            var offPeriods = await db.SystemEvents
                .AsNoTracking()
                .Where(e => (e.EventType == SystemEventTypes.Sleep || e.EventType == SystemEventTypes.Shutdown)
                    && e.Timestamp >= todayStart && e.Timestamp <= todayEnd)
                .Select(e => new { e.Timestamp, e.DurationSeconds })
                .ToListAsync();

            var totalSleepSec = offPeriods.Sum(p => p.DurationSeconds);
            _offLabel.Text = totalSleepSec > 0
                ? I18nService._("statusWindow.offDuration", FmtDur(totalSleepSec))
                : "";

            // Focus changes for today
            var changes = await db.FocusChanges
                .AsNoTracking()
                .Where(f => f.Timestamp >= todayStart && f.Timestamp <= todayEnd)
                .OrderBy(f => f.Timestamp)
                .Select(f => new { f.ProcessName, f.DurationSeconds, f.Timestamp })
                .ToListAsync();

            // Exclude off-period records
            var filtered = changes
                .Where(f => !offPeriods.Any(p =>
                    f.Timestamp >= p.Timestamp && f.Timestamp < p.Timestamp.AddSeconds(p.DurationSeconds)))
                .ToList();

            // Summary: group by process, top 5
            var summary = filtered
                .GroupBy(f => f.ProcessName)
                .Select(g => new
                {
                    ProcessName = g.Key,
                    TotalSeconds = g.Sum(f => f.DurationSeconds),
                    SwitchCount = g.Count()
                })
                .OrderByDescending(x => x.TotalSeconds)
                .Take(5)
                .ToList();

            // Adjusted switch counts (merge consecutive same-process entries)
            var adjSwitches = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string? prevProc = null;
            foreach (var f in filtered)
            {
                if (f.ProcessName != prevProc)
                    adjSwitches[f.ProcessName] = adjSwitches.GetValueOrDefault(f.ProcessName) + 1;
                prevProc = f.ProcessName;
            }

            _topList.BeginUpdate();
            _topList.Items.Clear();
            foreach (var item in summary)
            {
                var lvi = new ListViewItem(item.ProcessName);
                lvi.SubItems.Add(FmtDur(item.TotalSeconds));
                var sc = settings.Settings.MergeSameProcessSwitches
                    ? adjSwitches.GetValueOrDefault(item.ProcessName, item.SwitchCount)
                    : item.SwitchCount;
                lvi.SubItems.Add(sc.ToString());
                _topList.Items.Add(lvi);
            }
            _topList.EndUpdate();

            // 3. Tracking status
            var enabled = settings.Settings.TrackingEnabled;
            _statusLabel.Text = enabled
                ? I18nService._("statusWindow.trackingRunning")
                : I18nService._("statusWindow.trackingPaused");
            _statusLabel.ForeColor = enabled ? Color.DarkGreen : Color.DarkOrange;
            _toggleButton.Text = enabled
                ? I18nService._("common.pauseTracking")
                : I18nService._("common.resumeTracking");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StatusWindow refresh error: {ex.Message}");
            _statusLabel.Text = I18nService._("statusWindow.noResponse");
            _statusLabel.ForeColor = Color.Red;
        }
    }

    private void ToggleTracking(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsService>();
        settings.Settings.TrackingEnabled = !settings.Settings.TrackingEnabled;
        settings.Save();
        _ = RefreshData(services);
    }

    private static string FmtDur(double s)
    {
        if (s < 60) return I18nService._("time.seconds", s.ToString("F0"));
        if (s < 3600) return I18nService._("time.minutes", (s / 60).ToString("F1"));
        return I18nService._("time.hours", (s / 3600).ToString("F1"));
    }

    private static string Truncate(string text, int n)
        => text.Length <= n ? text : text[..(n - 3)] + "...";
}
