using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using KeyTrail.Core;
using KeyTrail.Desktop;

internal static class PerformanceProbe
{
    public static int Run(string directory)
    {
        var root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        var app = new App(renderOnly: true); app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        using var controller = new StatsController(Path.Combine(root, "fixture-" + Guid.NewGuid().ToString("N")), demo: true);
        var window = new MainWindow(controller) { Width = 1280, Height = 900 };
        app.MainWindow = window; window.Show(); Pump(window);
        for (var i = 0; i < 10; i++) { Press(controller); window.Refresh(); Pump(window); }
        var idle = Measure(window, () => window.Refresh());
        var active = Measure(window, () => { Press(controller); window.Refresh(); });
        var process = Process.GetCurrentProcess(); process.Refresh();
        var result = new
        {
            Iterations = 200,
            IdleMilliseconds = idle.Milliseconds, IdleAllocatedBytes = idle.Bytes,
            ActiveMilliseconds = active.Milliseconds, ActiveAllocatedBytes = active.Bytes,
            VisiblePrivateBytes = process.PrivateMemorySize64, VisibleWorkingSetBytes = process.WorkingSet64
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "refresh.json"), json); Console.WriteLine(json);
        window.AllowClose = true; window.Close();
        return 0;
    }
    private static void Press(StatsController controller)
    {
        controller.Accumulator.Handle(0x41, 0x1e, false, true, false, DateTime.Now);
        controller.Accumulator.Handle(0x41, 0x1e, false, false, false, DateTime.Now);
        controller.Accumulator.HandleMouse(0x204, 0, false, DateTime.Now);
        controller.Accumulator.HandleMouse(0x205, 0, false, DateTime.Now);
    }
    private static (double Milliseconds, long Bytes) Measure(Window window, Action update)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++) { update(); Pump(window); }
        clock.Stop(); return (clock.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
