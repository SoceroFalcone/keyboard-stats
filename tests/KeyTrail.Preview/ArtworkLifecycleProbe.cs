using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KeyTrail.Desktop;
using KeyTrail.Desktop.Controls;

internal static class ArtworkLifecycleProbe
{
    internal static int Run(string directory)
    {
        var root = Path.GetFullPath(directory); Directory.CreateDirectory(root);
        var app = new App(renderOnly: true); app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        using var controller = new StatsController(Path.Combine(root, "fixture-" + Guid.NewGuid().ToString("N")), demo: true);
        app.AttachController(controller);
        var (weak, keyboardWeak, images, bytes) = OpenAndRetire(app);
        for (var i = 0; i < 10 && (weak.IsAlive || keyboardWeak.IsAlive || images.Any(image => image.IsAlive)); i++)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(100);
        }
        var result = new { ArtworkPixelBytesWhileVisible = bytes, ArtworkReleasedAfterClosing = !weak.IsAlive && !keyboardWeak.IsAlive,
            ImageSourcesChecked = images.Length, AllImageSourcesReleased = images.All(image => !image.IsAlive), DashboardReleased = app.MainWindow is null };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "artwork-lifecycle.json"), json); Console.WriteLine(json);
        if (weak.IsAlive || keyboardWeak.IsAlive || images.Length != 95 || images.Any(image => image.IsAlive) || app.MainWindow is not null)
            throw new Exception("Keyboard/mouse artwork stayed reachable after retiring the dashboard");
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Weak, WeakReference KeyboardWeak, WeakReference[] Images, long Bytes) OpenAndRetire(App app)
    {
        var window = app.OpenDashboard(); window.Show(); window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var mouse = (MouseHeatmap)window.FindName("MouseButtons");
        var keyboard = (KeyboardHeatmap)window.FindName("Keyboard");
        var weak = new WeakReference(mouse); var keyboardWeak = new WeakReference(keyboard);
        var bytes = mouse.RetainedArtworkPixelBytes + keyboard.RetainedArtworkPixelBytes;
        var images = ImageSources(mouse).Concat(ImageSources(keyboard)).Select(source => new WeakReference(source)).ToArray();
        window.Hide(); app.ReleaseDashboard();
        return (weak, keyboardWeak, images, bytes);
    }

    private static IEnumerable<ImageSource> ImageSources(DependencyObject element)
    {
        if (element is Image image && image.Source is not null) yield return image.Source;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var source in ImageSources(VisualTreeHelper.GetChild(element, i))) yield return source;
    }
}
