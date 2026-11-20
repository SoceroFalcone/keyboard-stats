using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyTrail.Desktop.Controls;

public sealed class KeyboardHeatmap : UserControl
{
    private readonly KeyboardArtwork _artwork = new();
    private readonly Dictionary<string, KeyboardRegionButton> _keys = new();
    private readonly Canvas _canvas;
    private readonly TextBlock _fnLabel = new();
    private DayStats? _stats;
    private string? _selected;
    private readonly Dictionary<string, (long Count, int Level, bool Selected)> _painted = new();
    public event Action<string>? KeySelected;
    internal event Action<string?, MouseEventArgs>? KeyHovered;
    internal double ArtworkAspectRatio => _canvas.Width / _canvas.Height;
    internal long RetainedArtworkPixelBytes => _artwork.Plate.PixelWidth * (long)_artwork.Plate.PixelHeight * 4 + _artwork.Surfaces.Sum(s => s.RetainedPixelBytes);

    public KeyboardHeatmap()
    {
        _canvas = new Canvas { Width = _artwork.Plate.PixelWidth, Height = _artwork.Plate.PixelHeight };
        _canvas.Children.Add(new Image { Source = _artwork.Plate, Width = _canvas.Width, Height = _canvas.Height, IsHitTestVisible = false });
        Content = new Viewbox { Child = _canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
        foreach (var surface in _artwork.Surfaces)
        {
            var id = surface.Id;
            FrameworkElement element;
            if (id == "Fn")
            {
                var fn = new Grid { Width = surface.Bounds.Width, Height = surface.Bounds.Height };
                fn.Children.Add(new System.Windows.Shapes.Path { Data = surface.Shape, Fill = Brushes.Transparent });
                fn.Children.Add(new Image { Source = surface.Texture(0), IsHitTestVisible = false });
                _fnLabel = new TextBlock { Text = "Fn", Foreground = UiResources.Brush("#181B21"), FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                fn.Children.Add(_fnLabel); AutomationProperties.SetName(fn, "Fn，硬件功能键，不单独统计"); element = fn;
            }
            else
            {
                var key = KeyCatalog.Get(id);
                var button = new KeyboardRegionButton { Tag = id, Shape = surface.Shape, Width = surface.Bounds.Width, Height = surface.Bounds.Height,
                    MinHeight = 0, Padding = new Thickness(0), FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontWeight = FontWeights.SemiBold,
                    Content = id switch { "Backspace" => "Bksp", "CapsLock" => "Caps", "VolumeMute" => "静音", _ => key.Label } };
                button.Click += (_, _) => { SelectKey(id); KeySelected?.Invoke(id); };
                element = button; _keys.Add(id, button);
            }
            element.MouseEnter += (_, e) => KeyHovered?.Invoke(id, e);
            element.MouseMove += (_, e) => KeyHovered?.Invoke(id, e);
            element.MouseLeave += (_, e) => KeyHovered?.Invoke(null, e);
            Canvas.SetLeft(element, surface.Bounds.X); Canvas.SetTop(element, surface.Bounds.Y); _canvas.Children.Add(element);
        }
        SizeChanged += (_, _) => AdjustLabelSizes();
        Update(DayStats.Empty(DateOnly.FromDateTime(DateTime.Today)));
    }

    public void Update(DayStats day)
    {
        _stats = day;
        var max = Math.Max(1, day.Keys.Where(x => !MouseCatalog.IsMouse(x.Key)).Select(x => x.Value).DefaultIfEmpty().Max());
        foreach (var surface in _artwork.Surfaces)
        {
            var id = surface.Id; if (!_keys.TryGetValue(id, out var button)) continue;
            var count = day.Keys.GetValueOrDefault(id); var level = HeatScale.Level(count, max); var selected = id == _selected;
            if (_painted.TryGetValue(id, out var before) && before == (count, level, selected)) continue;
            button.HeatTexture = surface.Texture(level); button.HeatLevel = level;
            button.Background = HeatScale.Colors[level]; button.Foreground = UiResources.Brush(level >= 3 ? "#FFFFFF" : "#181B21");
            button.IsSelected = selected; var name = $"{KeyCatalog.Get(id).Name} · {count:N0} 次";
            AutomationProperties.SetName(button, name); _painted[id] = (count, level, selected);
        }
    }

    internal void SelectKey(string? id) { _selected = id; if (_stats is not null) Update(_stats); }

    private void AdjustLabelSizes()
    {
        var scale = Math.Max(.2, Math.Min(ActualWidth / _canvas.Width, ActualHeight / _canvas.Height));
        foreach (var button in _keys.Values)
        {
            var label = (string)button.Content; var size = (label.Length >= 4 ? 9.5 : 11) / scale;
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), size, button.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var available = button.Shape.Bounds.Width - 6;
            button.FontSize = Math.Min(size, size * available / Math.Max(1, text.WidthIncludingTrailingWhitespace));
        }
        _fnLabel.FontSize = 10 / scale;
    }
}

public sealed class KeyboardRegionButton : Button
{
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(Geometry), typeof(KeyboardRegionButton));
    public static readonly DependencyProperty HeatTextureProperty = DependencyProperty.Register(nameof(HeatTexture), typeof(BitmapSource), typeof(KeyboardRegionButton));
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(KeyboardRegionButton), new PropertyMetadata(false));
    public Geometry Shape { get => (Geometry)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public BitmapSource HeatTexture { get => (BitmapSource)GetValue(HeatTextureProperty); set => SetValue(HeatTextureProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    internal int HeatLevel { get; set; }
    public KeyboardRegionButton() => SetResourceReference(StyleProperty, "KeyboardRegionButton");
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => Shape?.FillContains(parameters.HitPoint) == true
        ? new PointHitTestResult(this, parameters.HitPoint) : null;
}
