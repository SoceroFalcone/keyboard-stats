namespace KeyTrail.Core;

// This class holds aggregates only. No text, ordered event queue or per-key timestamps are retained.
public sealed class KeyAccumulator
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pressed = new(StringComparer.Ordinal);
    private Dictionary<DateOnly, DayStats> _pending = new();
    private bool _paused;
    private bool _countRepeats;
    public bool Paused { get { lock (_gate) return _paused; } set { lock (_gate) _paused = value; } }
    public bool CountRepeats { get { lock (_gate) return _countRepeats; } set { lock (_gate) _countRepeats = value; } }

    public void Handle(int vk, int scan, bool extended, bool down, bool injected, DateTime time)
    {
        if (injected) return;
        var key = KeyCatalog.Resolve(vk, scan, extended);
        if (key is null) return;
        Record(key, down, time, mouse: false);
    }

    public void HandleMouse(int message, uint mouseData, bool injected, DateTime time)
    {
        if (injected) return;
        var key = MouseCatalog.Resolve(message, mouseData, out var down);
        if (key is not null) Record(key, down, time, mouse: true);
    }

    private void Record(string key, bool down, DateTime time, bool mouse)
    {
        lock (_gate)
        {
            if (!down) { _pressed.Remove(key); return; }
            // Pause may not emit a break event on some keyboard drivers.
            var firstDown = key == "Pause" || _pressed.Add(key);
            if (_paused || (!firstDown && (mouse || !_countRepeats))) return;
            var date = DateOnly.FromDateTime(time);
            if (!_pending.TryGetValue(date, out var day)) _pending[date] = day = DayStats.Empty(date);
            day.Add(key, time);
        }
    }

    public IReadOnlyList<DayStats> Drain()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return Array.Empty<DayStats>();
            var result = _pending.Values.ToArray();
            _pending = new();
            return result;
        }
    }

    public void ResetPressed() { lock (_gate) _pressed.Clear(); }
}
