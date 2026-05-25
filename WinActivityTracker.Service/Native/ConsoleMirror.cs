// Singleton TextWriter that captures all Console output into a buffer.
// Registered via Console.SetOut() BEFORE WebApplication.Build() so the ASP.NET
// console logger writes through this, not the original stdout.
//
// Notifies subscribers (ConsoleWindow) at most once per ~50ms to avoid
// flooding the UI thread with BeginInvoke on every character.
using System.Text;

namespace WinActivityTracker.Service.Native;

public class ConsoleMirror : TextWriter
{
    private readonly StringBuilder _buffer = new();
    private readonly TextWriter _original;
    private readonly object _lock = new();
    private Action? _listener;
    private volatile bool _dirty;
    private readonly System.Timers.Timer _timer;

    public override Encoding Encoding => Encoding.UTF8;

    public ConsoleMirror(TextWriter original)
    {
        _original = original;
        _timer = new System.Timers.Timer(50) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
        {
            if (_dirty)
            {
                _dirty = false;
                Action? listener;
                lock (_lock) { listener = _listener; }
                listener?.Invoke();
            }
        };
    }

    public void Subscribe(Action listener)
    {
        lock (_lock) { _listener += listener; }
        _timer.Start();
        // Immediately show existing content
        listener();
    }

    public void Unsubscribe(Action listener)
    {
        lock (_lock)
        {
            _listener -= listener;
            if (_listener == null) _timer.Stop();
        }
    }

    public string GetHistory()
    {
        lock (_lock) { return _buffer.ToString(); }
    }

    public override void Write(char value)
    {
        lock (_lock) { _buffer.Append(value); }
        _original.Write(value);
        _dirty = true;
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (_lock) { _buffer.Append(value); }
        _original.Write(value);
        _dirty = true;
    }
}
