// Singleton TextWriter that captures all Console output into a capped buffer.
// Registered before WebApplication.Build() so the ASP.NET console logger
// writes through this instead of the original stdout.
//
// Memory model:
//   - StringBuilder capped at 80K chars; excess trimmed at newline boundaries.
//   - Subscribers notified at most once per 200ms.
//   - GetHistory() returns a cached string, rebuilt only when dirty.
//   - Writes are also tee'd to the original stdout.
using System.Text;

namespace WinActivityTracker.Service.Native;

public class ConsoleMirror : TextWriter
{
    private const int MaxChars = 80_000;
    private const int NotifyMs = 200;

    private readonly StringBuilder _buffer = new();
    private readonly TextWriter _original;
    private readonly object _lock = new();
    private Action? _listener;
    private bool _dirty;
    private bool _notificationPending;
    private bool _disposed;
    private string _cached = string.Empty;
    private readonly ITimer _timer;

    public override Encoding Encoding => Encoding.UTF8;

    public ConsoleMirror(TextWriter original) : this(original, TimeProvider.System) { }

    internal ConsoleMirror(TextWriter original, TimeProvider timeProvider)
    {
        _original = original;
        _timer = timeProvider.CreateTimer(_ => NotifyListeners(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                _disposed = true;
                _listener = null;
                _notificationPending = false;
            }
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }

    public void Subscribe(Action listener)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _listener += listener;
        }
        listener();
    }

    public void Unsubscribe(Action listener)
    {
        lock (_lock)
        {
            _listener -= listener;
            if (_listener == null && !_disposed)
            {
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _notificationPending = false;
            }
        }
    }

    // Returns a cached string — avoids allocating on every UI refresh.
    public string GetHistory()
    {
        lock (_lock)
        {
            if (_dirty)
            {
                _cached = _buffer.ToString();
                _dirty = false;
            }
            return _cached;
        }
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
            if (!_disposed)
            {
                _buffer.Append(value);
                OnAppendedLocked();
            }
        }
        _original.Write(value);
    }

    private void Append(string text)
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _buffer.Append(text);
                OnAppendedLocked();
            }
        }
        _original.Write(text);
    }

    private void OnAppendedLocked()
    {
        TrimExcessLocked();
        _dirty = true;
        // No UI subscriber means no timer wakeup and no history-string copy.
        // Do not restart an armed timer: continuous logging must still refresh.
        if (_listener != null && !_notificationPending)
        {
            _notificationPending = true;
            _timer.Change(TimeSpan.FromMilliseconds(NotifyMs), Timeout.InfiniteTimeSpan);
        }
    }

    private void NotifyListeners()
    {
        Action? listener;
        lock (_lock)
        {
            if (_disposed || !_notificationPending) return;
            _notificationPending = false;
            listener = _listener;
        }
        listener?.Invoke();
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
