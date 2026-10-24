namespace WinActivityTracker.Core.Services;

/// <summary>A thread-safe LRU cache for recomputable process metadata.</summary>
internal sealed class BoundedCache<TValue>(int capacity)
{
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, TValue>>> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<KeyValuePair<string, TValue>> _recent = new();
    private readonly object _lock = new();

    public bool TryGetValue(string key, out TValue value)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddLast(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Set(string key, TValue value)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                node.Value = new(key, value);
                _recent.AddLast(node);
                return;
            }
            if (_entries.Count >= capacity)
            {
                var oldest = _recent.First!;
                _entries.Remove(oldest.Value.Key);
                _recent.RemoveFirst();
            }
            _entries.Add(key, _recent.AddLast(new KeyValuePair<string, TValue>(key, value)));
        }
    }

    public KeyValuePair<string, TValue>[] Snapshot()
    {
        lock (_lock) return _recent.ToArray();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _recent.Clear();
        }
    }
}
