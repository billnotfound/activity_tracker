// Singleton TextWriter that captures Console output into a capped string buffer.
// Registered BEFORE WebApplication.Build() so the ASP.NET console logger pipes through here.
//
// Memory model:
//   - Lines are stored in a List<char> with a running total length.
//   - When total exceeds MaxChars (250KB), the oldest content is trimmed.
//   - Subscribers are notified at most once per ~200ms.
//   - GetHistory() returns a cached string; rebuilt only when dirty.
using System.Text;

namespace WinActivityTracker.Service.Native;

public class ConsoleMirror : TextWriter
{
    private const int MaxChars = 250_000;
    private const int NotifyMs = 200;

    private readonly StringBuilder _buffer = new();
    private readonly TextWriter _original;
    private readonly object _lock = new();
    private Action? _listener;
    private volatile bool _dirty;
    private string _cached = string.Empty;
    private readonly System.Timers.Timer _timer;

    public override Encoding Encoding => Encoding.UTF8;

    public ConsoleMirror(TextWriter original)
    {
        _original = original;
        _timer = new System.Timers.Timer(NotifyMs) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
        {
            if (_dirty)
            {
                _dirty = false;
                lock (_lock) { _cached = _buffer.ToString(); }
                _listener?.Invoke();
            }
        };
    }

    public void Subscribe(Action listener)
    {
        lock (_lock)
        {
            _listener += listener;
            if (_buffer.Length > 0) _cached = _buffer.ToString();
        }
        _timer.Start();
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

    // Returns a cached string — avoids allocating on every UI refresh.
    public string GetHistory()
    {
        lock (_lock) { return _cached; }
    }

    public override void Write(char value) => Append(value.ToString());

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Append(value);
    }

    private void Append(string text)
    {
        lock (_lock)
        {
            _buffer.Append(text);
            // Trim from the beginning if buffer exceeds max
            if (_buffer.Length > MaxChars)
            {
                var excess = _buffer.Length - MaxChars + MaxChars / 4; // trim 25% extra to avoid frequent trims
                // Find next newline after the excess point for clean cut
                var cutAt = excess;
                while (cutAt < _buffer.Length && _buffer[cutAt] != '\n') cutAt++;
                if (cutAt < _buffer.Length) cutAt++; // include the newline
                _buffer.Remove(0, Math.Min(cutAt, _buffer.Length));
            }
        }
        _original.Write(text);
        _dirty = true;
    }
}
