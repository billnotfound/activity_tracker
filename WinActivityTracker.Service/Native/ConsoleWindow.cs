// In-app console window — displays Console output captured by ConsoleMirror.
// ConsoleMirror batches updates every ~200ms; re-read GetHistory() on each
// tick and touch the TextBox only if content changed.
//
// Closing this window does not exit the application (it's just a Form).
// All output is also tee'd to the original stdout.
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public class ConsoleWindow : Form
{
    private readonly TextBox _output;
    private readonly ConsoleMirror _mirror;
    private readonly Action _onTick;
    private bool _subscribed;
    private int _refreshPending;

    public ConsoleWindow(ConsoleMirror mirror)
    {
        _mirror = mirror;

        Text = I18nService._("consoleWindow.title");
        Size = new Size(950, 600);
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Padding = new Padding(8);
        Icon = IconHelper.GetTimerIcon();

        _output = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGray,
            WordWrap = true
        };
        Controls.Add(_output);

        // Timer callback: reads the buffer and updates the TextBox. Runs on the
        // timer thread; BeginInvoke marshals to the UI thread.
        _onTick = () =>
        {
            if (_output.IsDisposed || Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
            try
            {
                if (_output.InvokeRequired)
                    _output.BeginInvoke(RefreshOutput);
                else
                    RefreshOutput();
            }
            catch { Interlocked.Exchange(ref _refreshPending, 0); }
        };

        VisibleChanged += (_, _) => UpdateSubscription();
        Resize += (_, _) => UpdateSubscription();
    }

    private void RefreshOutput()
    {
        // A busy UI gets one pending callback, without retaining an 80K snapshot
        // for every log tick. Read the newest history only when it can be shown.
        try
        {
            if (!IsDisposed && Visible && WindowState != FormWindowState.Minimized)
                SetText(_mirror.GetHistory());
        }
        finally { Interlocked.Exchange(ref _refreshPending, 0); }
    }

    private void UpdateSubscription()
    {
        var shouldSubscribe = !IsDisposed && !Disposing && Visible
            && WindowState != FormWindowState.Minimized;
        if (shouldSubscribe == _subscribed) return;
        _subscribed = shouldSubscribe;
        if (shouldSubscribe) _mirror.Subscribe(_onTick);
        else _mirror.Unsubscribe(_onTick);
    }

    private void SetText(string text)
    {
        if (_output.IsDisposed || !Visible || WindowState == FormWindowState.Minimized) return;
        if (_output.Text == text) return; // no change, skip flicker
        var wasAtEnd = _output.SelectionStart >= _output.TextLength - 100;
        _output.Text = text;
        if (wasAtEnd)
        {
            _output.SelectionStart = _output.TextLength;
            _output.ScrollToCaret();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mirror.Unsubscribe(_onTick);
            _subscribed = false;
        }
        base.Dispose(disposing);
    }
}
