// In-app console window — displays Console output captured by ConsoleMirror.
// ConsoleMirror batches updates every ~200ms; we re-read GetHistory() on each
// tick and only touch the TextBox if content changed.
//
// Closing this window does NOT exit the application (it's just a Form).
// All output is also tee'd to the original stdout for terminal visibility.
namespace WinActivityTracker.Service.Native;

public class ConsoleWindow : Form
{
    private readonly TextBox _output;
    private readonly ConsoleMirror _mirror;
    private readonly Action _onTick;

    public ConsoleWindow(int apiPort, ConsoleMirror mirror)
    {
        _mirror = mirror;

        Text = "WinActivityTracker — 控制台";
        Size = new Size(700, 500);
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Padding = new Padding(8);

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

        // Timer callback — reads the full buffer and updates the TextBox.
        // Runs on the timer thread; uses BeginInvoke for UI thread safety.
        _onTick = () =>
        {
            if (_output.IsDisposed) return;
            var text = mirror.GetHistory();
            try
            {
                if (_output.InvokeRequired)
                    _output.BeginInvoke(() => SetText(text));
                else
                    SetText(text);
            }
            catch { }
        };

        mirror.Subscribe(_onTick);
    }

    private void SetText(string text)
    {
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
            _mirror.Unsubscribe(_onTick);
        base.Dispose(disposing);
    }
}
