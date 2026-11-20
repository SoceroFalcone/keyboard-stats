using System.Collections.Concurrent;
using System.Windows.Media;

namespace KeyTrail.Desktop.Controls;

internal static class UiResources
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Brushes = new();
    internal static SolidColorBrush Brush(string hex) => Brushes.GetOrAdd(hex, static color =>
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    });
}
