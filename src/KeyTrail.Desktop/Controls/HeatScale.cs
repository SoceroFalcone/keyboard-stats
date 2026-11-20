using System.Windows.Media;

namespace KeyTrail.Desktop.Controls;

internal static class HeatScale
{
    internal static readonly Brush[] Colors = [UiResources.Brush("#EEF0F3"), UiResources.Brush("#D8E2EE"),
        UiResources.Brush("#A8C2DD"), UiResources.Brush("#3B608F"), UiResources.Brush("#1F3A5C")];
    internal static int Level(long value, long maximum) => value <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(Math.Sqrt((double)value / Math.Max(1, maximum)) * 4), 1, 4);
}
