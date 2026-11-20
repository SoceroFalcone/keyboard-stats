using KeyTrail.Desktop.Native;

namespace KeyTrail.Desktop;

public sealed class StatsController : IDisposable
{
    private readonly object _collectGate = new();
    private readonly InputMonitor? _monitor;
    private readonly System.Threading.Timer _timer;
    private readonly List<DayStats> _retry = new();
    private bool _disposed;
    public KeyAccumulator Accumulator { get; } = new();
    public StatsStore Store { get; }
    public AppSettings Settings { get; }
    public string DataRoot { get; }
    public string? Error { get; private set; }
    public bool IsDemo { get; }
    public bool Listening => !IsDemo && _monitor?.IsRunning == true && !Accumulator.Paused && Error is null;

    public StatsController(string dataRoot, bool demo)
    {
        IsDemo = demo;
        DataRoot = dataRoot;
        Directory.CreateDirectory(dataRoot);
        Store = new StatsStore(Path.Combine(dataRoot, "days"));
        Settings = AppSettings.Load(dataRoot);
        Accumulator.CountRepeats = Settings.CountRepeats;
        Accumulator.Paused = Settings.Paused;
        // Fail visibly before collecting if today's persistent statistics cannot be loaded.
        Store.GetDay(DateOnly.FromDateTime(DateTime.Now));
        if (demo) SeedDemo();
        else
        {
            _monitor = new InputMonitor(Accumulator);
            try { _monitor.Start(); }
            catch (Exception ex) { Error = ex.Message + " " + ex.InnerException?.Message; Accumulator.Paused = true; }
        }
        _timer = new System.Threading.Timer(_ => SaveInBackground(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Collect()
    {
        lock (_collectGate)
        {
            if (_disposed) return;
            if (_monitor is not null && (!_monitor.IsRunning || _monitor.LastError is not null))
            { Error ??= _monitor.LastError ?? "键鼠监听已停止，请重新启动应用。"; Accumulator.Paused = true; }
            _retry.AddRange(Accumulator.Drain());
            if (_retry.Count == 0) return;
            try { Store.Merge(_retry); _retry.Clear(); }
            catch (Exception ex) { Error = ex.Message; Accumulator.Paused = true; }
        }
    }

    public void Flush() { Collect(); Store.Flush(); }
    private void SaveInBackground()
    {
        try { Flush(); }
        catch (Exception ex) { Error = "数据暂未保存：" + ex.Message; Accumulator.Paused = true; }
    }

    public void SetPaused(bool paused)
    {
        if (!paused && Error is not null) throw new InvalidOperationException("请先解决提示的问题并重新启动应用，再恢复统计。");
        Accumulator.Paused = paused;
        Settings.Paused = paused;
        Settings.Save(DataRoot);
        Task.Run(SaveInBackground);
    }

    public void UpdateSettings() { Accumulator.CountRepeats = Settings.CountRepeats; Settings.Save(DataRoot); }

    private void SeedDemo()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (Store.GetDay(today).Total > 0) return;
        var random = new Random(73);
        var frequent = new[] { "Space", "E", "A", "S", "T", "Enter", "Backspace", "LControl", "R", "N", "I", "O", "D", "LShift" };
        var hours = new[] { 8, 9, 9, 10, 10, 11, 13, 14, 14, 14, 15, 16, 17, 18, 20, 21 };
        var keys = KeyCatalog.All.Take(104).ToArray();
        for (var d = 13; d >= 0; d--)
        {
            var date = today.AddDays(-d);
            if (Store.GetDay(date).Total > 0) continue;
            var day = DayStats.Empty(date);
            var count = d == 0 ? 12846 : random.Next(2800, 15000);
            for (var i = 0; i < count; i++)
            {
                var hour = hours[random.Next(16)];
                var key = random.NextDouble() < .73 ? frequent[random.Next(frequent.Length)] : keys[random.Next(104)].Id;
                day.Add(key, date.ToDateTime(new TimeOnly(hour, random.Next(18))));
            }
            var mouseCounts = new[] { 1840, 260, 96, 54, 32 };
            for (var button = 0; button < MouseCatalog.All.Count; button++)
                for (var i = 0; i < mouseCounts[button] * (18 - d) / 18; i++)
                    day.Add(MouseCatalog.All[button].Id, date.ToDateTime(new TimeOnly(hours[(i + button) % hours.Length], i % 60)));
            Store.Merge(new[] { day });
        }
        Store.Flush();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _monitor?.Dispose();
        lock (_collectGate)
        {
            Flush();
            _disposed = true;
        }
    }
}
