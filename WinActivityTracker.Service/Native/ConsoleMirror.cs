// Singleton TextWriter that captures all Console output into a capped buffer.
// Registered BEFORE WebApplication.Build() so the ASP.NET console logger
// writes through this, not the original stdout.
//
// Memory model:
//   - StringBuilder capped at 80K chars; excess trimmed at newline boundaries.
//   - Subscribers notified at most once per 200ms.
//   - GetHistory() returns a cached string; rebuilt only when dirty.
//   - Writes are tee'd to the original stdout.
using System.Text;

namespace WinActivityTracker.Service.Native;

public class ConsoleMirror : TextWriter, IDisposable
{
    private const int MaxChars = 80_000;
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
        _timer = new System.Timers.Timer(NotifyMs) { AutoReset = false };
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

    void IDisposable.Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
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

    public override void Write(char value) => Append(value);

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Append(value);
    }

    private void Append(char value)
    {
        lock (_lock)
        {
            _buffer.Append(value);
            TrimExcessLocked();
        }
        _original.Write(value);
        _dirty = true;
        _timer.Stop(); _timer.Start();
    }

    private void Append(string text)
    {
        lock (_lock)
        {
            _buffer.Append(text);
            TrimExcessLocked();
        }
        _original.Write(text);
        _dirty = true;
        _timer.Stop(); _timer.Start();
    }

    private void TrimExcessLocked()
    {
        if (_buffer.Length > MaxChars)
        {
            var excess = _buffer.Length - MaxChars + MaxChars / 4;
            var cutAt = excess;
            while (cutAt < _buffer.Length && _buffer[cutAt] != '\n') cutAt++;
            if (cutAt < _buffer.Length) cutAt++;
            _buffer.Remove(0, Math.Min(cutAt, _buffer.Length));
        }
    }
}
