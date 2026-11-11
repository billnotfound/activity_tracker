// Live status window — current focus, Top 5, tracking controls.
// All data accessed directly via DI / EF Core — no HTTP dependency on Kestrel.
using Microsoft.EntityFrameworkCore;
using WinActivityTracker.Core.Data;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;
using WinActivityTracker.Core.Trackers;
using WinActivityTracker.Service.Api;

namespace WinActivityTracker.Service.Native;

public partial class StatusWindow : Form
{
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private CancellationTokenSource? _refreshCts;
    private bool _refreshInProgress;

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
        VisibleChanged += (_, _) => UpdateRefreshState(services);
        Resize += (_, _) => UpdateRefreshState(services);
    }

    private bool CanRefresh => !IsDisposed && !Disposing && Visible
        && WindowState != FormWindowState.Minimized;

    private void UpdateRefreshState(IServiceProvider services)
    {
        if (!CanRefresh)
        {
            _refreshTimer.Stop();
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
            return;
        }

        if (_refreshCts != null) return;
        _refreshCts = new CancellationTokenSource();
        _refreshTimer.Start();
        _ = RefreshData(services);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
        }
        base.Dispose(disposing);
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
        if (!CanRefresh || _refreshInProgress || _refreshCts == null) return;
        _refreshInProgress = true;
        var cancellationToken = _refreshCts.Token;
        try
        {
            var processCache = services.GetRequiredService<ProcessNameCache>();
            var tagService = services.GetRequiredService<TagService>();
            var hiddenRules = tagService.GetHiddenRules();
            // Only strong idle rules (weight >= 10) affect focus records; weak
            // rules are overridden by foreground activity.
            var idleRules = tagService.GetIdleRules().Where(r => r.Weight >= 10).ToList();

            // 1. Current focus (direct P/Invoke)
            var windows = WindowTracker.EnumerateVisibleWindows(processCache);
            var focused = windows.FirstOrDefault(w =>
                w.IsFocused && !TagService.MatchesHidden(hiddenRules, w.ProcessName, w.Title)
                    && !TagService.MatchesIdle(idleRules, w.ProcessName, w.Title));
            _focusLabel.Text = focused != default
                ? $"{focused.ProcessName} — {Truncate(focused.Title, 60)}"
                : I18nService._("statusWindow.noFocusWindow");

            // 2. Today summary — direct EF Core query
            using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = services.GetRequiredService<SettingsService>();

            var localStart = settings.Settings.GetActivityDayStart(DateTime.Now);
            var todayStart = localStart.ToUniversalTime();
            var todayEnd = localStart.AddDays(1).ToUniversalTime();

            // Off periods (sleep/shutdown)
            var offPeriods = await FocusEndpoints.GetOffPeriods(db, todayStart, todayEnd, cancellationToken);

            if (!CanRefresh || cancellationToken.IsCancellationRequested) return;

            var totalSleepSec = offPeriods.Sum(p => p.DurationSeconds);
            _offLabel.Text = totalSleepSec > 0
                ? I18nService._("statusWindow.offDuration", FmtDur(totalSleepSec))
                : "";

            // Focus changes for today
            var changes = await LoadFocusRangeAsync(db, todayStart, todayEnd, offPeriods, cancellationToken);

            if (!CanRefresh || cancellationToken.IsCancellationRequested) return;

            // Off periods have been removed from durations. Filter hidden and idle records.
            var filtered = changes
                .Where(f => !TagService.MatchesHidden(hiddenRules, f.ProcessName, f.WindowTitle))
                .Where(f => !TagService.MatchesIdle(idleRules, f.ProcessName, f.WindowTitle))
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StatusWindow refresh error: {ex.Message}");
            if (!CanRefresh || cancellationToken.IsCancellationRequested) return;
            _statusLabel.Text = I18nService._("statusWindow.noResponse");
            _statusLabel.ForeColor = Color.Red;
        }
        finally { _refreshInProgress = false; }
    }

    internal static async Task<List<FocusChange>> LoadFocusRangeAsync(
        AppDbContext db, DateTime start, DateTime end, IReadOnlyList<TimeInterval> offPeriods,
        CancellationToken cancellationToken = default)
    {
        var focus = db.FocusChanges.AsNoTracking().Where(f => f.ProcessName != SystemMarkers.SystemSleepProcess);
        var changes = await focus
            .Where(f => f.Timestamp >= start && f.Timestamp < end)
            .OrderBy(f => f.Timestamp).ToListAsync(cancellationToken);
        var preceding = await focus
            .Where(f => f.Timestamp < start).OrderByDescending(f => f.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
        if (preceding != null && IntervalMath.SafeAddSeconds(preceding.Timestamp, preceding.DurationSeconds) > start)
            changes.Insert(0, preceding);

        // Clip sessions to this activity day and subtract sleep/shutdown spans.
        foreach (var row in changes)
        {
            var rowEnd = IntervalMath.SafeAddSeconds(row.Timestamp, row.DurationSeconds);
            if (rowEnd > end) rowEnd = end;
            var rowStart = row.Timestamp < start ? start : row.Timestamp;
            row.DurationSeconds = IntervalMath.Subtract(rowStart, rowEnd, offPeriods).Sum(segment => segment.DurationSeconds);
        }
        return changes.Where(row => row.DurationSeconds > 0).ToList();
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
