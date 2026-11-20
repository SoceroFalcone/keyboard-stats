using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace KeyTrail.Desktop.Controls;

// Only 24 reusable native controls. No chart axes, repeated construction or idle repaint.
public sealed class HourlyHeatmap : UniformGrid
{
    private readonly Button[] _cells = new Button[24];
    private readonly TextBlock[] _counts = new TextBlock[24];
    private readonly long[] _values = new long[24];
    private readonly int[] _levels = Enumerable.Repeat(-1, 24).ToArray();
    private int _selected = -1;
    public int SelectedHour => _selected;
    public event Action<int, long>? HourSelected;

    public HourlyHeatmap()
    {
        Rows = 2; Columns = 12;
        for (var hour = 0; hour < 24; hour++)
        {
            var index = hour;
            var count = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0),
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
            var display = new StackPanel();
            display.Children.Add(new TextBlock { Text = hour.ToString("00"), TextAlignment = TextAlignment.Center });
            display.Children.Add(count);
            var cell = new Button { Content = display, FontSize = 13, Padding = new Thickness(2, 0, 2, 0),
                MinHeight = 22, Margin = new Thickness(2), BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Segoe UI"), Tag = hour };
            cell.Click += (_, _) => SelectHour(index);
            _cells[hour] = cell;
            _counts[hour] = count;
            Children.Add(cell);
        }
        SizeChanged += (_, e) =>
        {
            var visibility = e.NewSize.Width >= 672 && e.NewSize.Height >= 96 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var count in _counts) count.Visibility = visibility;
        };
        SetData(new long[24]);
    }

    public void SetData(long[] values)
    {
        if (values.Length != 24) throw new ArgumentException("Expected 24 hourly totals.", nameof(values));
        var max = Math.Max(1, values.Max());
        for (var i = 0; i < 24; i++)
        {
            var level = HeatScale.Level(values[i], max);
            var cell = _cells[i];
            if (_levels[i] != level)
            {
                cell.Background = HeatScale.Colors[level];
                cell.Foreground = UiResources.Brush(level >= 3 ? "#FFFFFF" : "#181B21");
            }
            if (_values[i] != values[i] || _levels[i] < 0)
            {
                var text = $"{i:00}:00–{i + 1:00}:00 · {values[i]:N0} 次";
                _counts[i].Text = $"{values[i]:N0} 次";
                cell.ToolTip = text; AutomationProperties.SetName(cell, text);
            }
            _values[i] = values[i]; _levels[i] = level;
        }
    }

    private void SelectHour(int hour)
    {
        if (_selected >= 0) _cells[_selected].BorderThickness = new Thickness(0);
        _selected = hour;
        _cells[hour].BorderBrush = UiResources.Brush("#B8694A");
        _cells[hour].BorderThickness = new Thickness(2);
        HourSelected?.Invoke(hour, _values[hour]);
    }
}
