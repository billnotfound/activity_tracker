// Native settings form.
// Uses a simple TableLayoutPanel with fixed-height GroupBoxes — no AutoSize
// conflicts, no overlapping. Each GroupBox docks an inner panel for its content.
using System.Drawing;
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
    private CheckBox _mergeSwitchesCheck = null!;
    private Label _statusLabel = null!;


    public SettingsWindow(SettingsService settings, int apiPort)
    {
        _settings = settings;

        Text = I18nService._("settingsWindow.title");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(590, 760);
        Size = new Size(610, 780);
        ShowInTaskbar = true;
        Padding = new Padding(12);
        Icon = IconHelper.GetSettingsIcon();

        BuildUI();
        LoadSettings();
    }

    private void BuildUI()
    {

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
        };

        // --- Tracking checkbox ---
        _trackingCheck = new CheckBox
        {
            Text = I18nService._("settingsWindow.enableTracking"), AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
            Anchor = AnchorStyles.Left
        };
        main.Controls.Add(_trackingCheck);

        _mergeSwitchesCheck = new CheckBox
        {
            Text = I18nService._("settingsWindow.mergeSwitches"), AutoSize = true,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 8)
        };
        main.Controls.Add(_mergeSwitchesCheck);

        // === Helpers ===

        // GroupBox with inner Panel — fixed Height, Dock=Fill inner so content stretches.
        GroupBox MakeGroup(string title, int height)
        {
            var g = new GroupBox
            {
                Text = title,
                Height = height,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Margin = new Padding(0, 12, 0, 0)
            };
            var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 8, 4) };
            g.Controls.Add(inner);
            main.Controls.Add(g);
            return g;
        }

        // Row: label + NUD, placed manually inside the inner panel.
        void AddRow(Panel parent, int row, string label, out NumericUpDown nud, int min, int max, int def)
        {
            var y = 6 + row * 38;
            var lbl = new Label { Text = label, AutoSize = true, Location = new Point(0, y) };
            nud = new NumericUpDown { Minimum = min, Maximum = max, Value = def, Width = 100, Location = new Point(0, y - 1) };
            nud.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            parent.Controls.Add(lbl);
            parent.Controls.Add(nud);
        }

        // --- Polling (3 rows → height ~136) ---
        {
            var g = MakeGroup(I18nService._("settingsWindow.pollingGroup"), 156);
            var pan = g.Controls[0] as Panel;
            AddRow(pan!, 0, I18nService._("settingsWindow.windowPollLabel"), out _windowInterval, 1, 3600, 3);
            AddRow(pan!, 1, I18nService._("settingsWindow.processPollLabel"), out _processInterval, 5, 3600, 30);
            AddRow(pan!, 2, I18nService._("settingsWindow.mediaPollLabel"), out _mediaInterval, 1, 3600, 5);
        }

        // --- Idle (1 row → height ~64) ---
        {
            var g = MakeGroup(I18nService._("settingsWindow.idleGroup"), 78);
            var pan = g.Controls[0] as Panel;
            AddRow(pan!, 0, I18nService._("settingsWindow.idleThresholdLabel"), out _idleThreshold, 1, 120, 2);
        }

        // --- Exclusions (textbox → height ~58) ---
        {
            var g = MakeGroup(I18nService._("settingsWindow.exclusionsGroup"), 78);
            _excludedBox = new TextBox
            {
                Font = new Font("Consolas", 9),
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            g.Controls[0].Controls.Add(_excludedBox);
        }

        // --- Database (1 row → height ~58) ---
        {
            var g = MakeGroup(I18nService._("common.database"), 78);
            var pan = g.Controls[0] as Panel;
            AddRow(pan!, 0, I18nService._("settingsWindow.retentionLabel"), out _retentionDays, 1, 3650, 90);
        }

        // --- Server (1 row → height ~58) ---
        {
            var g = MakeGroup(I18nService._("settingsWindow.serverGroup"), 78);
            var pan = g.Controls[0] as Panel;
            AddRow(pan!, 0, I18nService._("settingsWindow.apiPortLabel"), out _apiPortInput, 1024, 65535, 5200);
        }

        // --- Buttons ---
        {
            var btnPanel = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                Anchor = AnchorStyles.Left, Margin = new Padding(0, 12, 0, 0)
            };
            var saveBtn = new Button { Text = I18nService._("common.save"), Size = new Size(120, 40), Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold) };
            saveBtn.Click += (_, _) => SaveSettings();
            var cancelBtn = new Button { Text = I18nService._("common.cancel"), Size = new Size(120, 40), Font = new Font("Microsoft YaHei UI", 10) };
            cancelBtn.Click += (_, _) => Close();
            _statusLabel = new Label { Text = "", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 0, 0) };
            btnPanel.Controls.Add(saveBtn);
            btnPanel.Controls.Add(cancelBtn);
            btnPanel.Controls.Add(_statusLabel);
            main.Controls.Add(btnPanel);
        }

        Controls.Add(main);
    }

    private void LoadSettings()
    {
        var s = _settings.Settings;
        _trackingCheck.Checked = s.TrackingEnabled;
        _mergeSwitchesCheck.Checked = s.MergeSameProcessSwitches;
        _windowInterval.Value = s.WindowPollSeconds;
        _processInterval.Value = s.ProcessPollSeconds;
        _mediaInterval.Value = s.MediaPollSeconds;
        _idleThreshold.Value = s.IdleThresholdMinutes;
        _excludedBox.Text = string.Join(", ", s.ExcludedProcesses);
        _retentionDays.Value = s.DataRetentionDays;
        _apiPortInput.Value = s.ApiPort;
    }

    private void SaveSettings()
    {
        var s = _settings.Settings;
        s.TrackingEnabled = _trackingCheck.Checked;
        s.MergeSameProcessSwitches = _mergeSwitchesCheck.Checked;
        s.WindowPollSeconds = (int)_windowInterval.Value;
        s.ProcessPollSeconds = (int)_processInterval.Value;
        s.MediaPollSeconds = (int)_mediaInterval.Value;
        s.IdleThresholdMinutes = (int)_idleThreshold.Value;
        s.ExcludedProcesses = _excludedBox.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        s.DataRetentionDays = (int)_retentionDays.Value;
        s.ApiPort = (int)_apiPortInput.Value;
        _settings.Save();
        _statusLabel.Text = I18nService._("settingsWindow.saved");
        _statusLabel.ForeColor = Color.DarkGreen;
    }

}
