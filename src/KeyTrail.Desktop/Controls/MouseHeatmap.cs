using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyTrail.Desktop.Controls;

public sealed class MouseHeatmap : Grid
{
    private readonly MouseArtwork _artwork = new();
    private readonly Dictionary<string, (MouseRegionButton Button, TextBlock Count, Border Row, Border Swatch)> _cells = new();
    private readonly Dictionary<string, (long Count, int Level, bool Selected)> _painted = new();
    private DayStats? _day;
    private string? _selected;
    public event Action<string>? ButtonSelected;
    internal event Action<string?, MouseEventArgs>? ButtonHovered;
    internal string? SelectedButton => _selected;
    internal double ArtworkAspectRatio { get; }
    public FrameworkElement Summary { get; }
    internal long RetainedArtworkPixelBytes => _artwork.Plate.PixelWidth * (long)_artwork.Plate.PixelHeight * 4 + _artwork.Surfaces.Sum(x => x.RetainedPixelBytes);

    public MouseHeatmap()
    {
        var canvas = new Canvas { Width = _artwork.Plate.PixelWidth, Height = _artwork.Plate.PixelHeight };
        canvas.Children.Add(new Image { Source = _artwork.Plate, Width = canvas.Width, Height = canvas.Height, IsHitTestVisible = false });
        var frame = UprightFrame(canvas, _artwork.Plate);
        ArtworkAspectRatio = frame.Width / frame.Height;
        var view = new Viewbox { Child = frame, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(view);
        var summary = new UniformGrid { Columns = 5 }; Summary = summary;

        foreach (var surface in _artwork.Surfaces)
        {
            var id = surface.Id;
            var button = new MouseRegionButton { Tag = id, Width = surface.Bounds.Width, Height = surface.Bounds.Height,
                Shape = surface.Shape, Cursor = Cursors.Hand, TabIndex = _cells.Count };
            Canvas.SetLeft(button, surface.Bounds.X); Canvas.SetTop(button, surface.Bounds.Y); canvas.Children.Add(button);
            var rowContent = new Grid();
            rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var swatch = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), BorderBrush = UiResources.Brush("#6B87B0"), BorderThickness = new Thickness(.6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            rowContent.Children.Add(swatch);
            var label = Text(KeyCatalog.Get(id).Label, 13, "#181B21"); SetColumn(label, 1); rowContent.Children.Add(label);
            var count = Text("0", 16, "#181B21"); count.FontFamily = new FontFamily("Segoe UI"); count.FontWeight = FontWeights.SemiBold;
            count.Margin = new Thickness(6, 0, 0, 0); count.TextAlignment = TextAlignment.Right; count.TextTrimming = TextTrimming.CharacterEllipsis;
            SetColumn(count, 2); rowContent.Children.Add(count);
            var row = new Border { Child = rowContent, Padding = new Thickness(9, 4, 9, 4), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 0) };
            summary.Children.Add(row); _cells[id] = (button, count, row, swatch);
            button.Click += (_, _) => { SelectButton(id); ButtonSelected?.Invoke(id); };
            button.MouseEnter += (_, e) => { PaintRow(id, true); ButtonHovered?.Invoke(id, e); };
            button.MouseMove += (_, e) => ButtonHovered?.Invoke(id, e);
            button.MouseLeave += (_, e) => { PaintRow(id, false); ButtonHovered?.Invoke(null, e); };
            // Rows supplement the part-shaped native buttons without adding duplicate tab stops.
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, e) => { SelectButton(id); ButtonSelected?.Invoke(id); e.Handled = true; };
            row.MouseEnter += (_, e) => { button.IsEmphasized = true; ButtonHovered?.Invoke(id, e); };
            row.MouseMove += (_, e) => ButtonHovered?.Invoke(id, e);
            row.MouseLeave += (_, e) => { button.IsEmphasized = false; ButtonHovered?.Invoke(null, e); };
        }
        Update(DayStats.Empty(DateOnly.FromDateTime(DateTime.Today)));
    }

    private static Canvas UprightFrame(Canvas canvas, BitmapSource plate)
    {
        // Rotate the existing plate AND hit regions together, retaining the verified left flank.
        // No second rotated bitmap is retained, and no animation or render timer is introduced.
        var rotation = new RotateTransform(Math.Atan2(500, 840) * 180 / Math.PI); rotation.Freeze();
        var rgba = new FormatConvertedBitmap(plate, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[plate.PixelWidth * plate.PixelHeight * 4]; rgba.CopyPixels(pixels, plate.PixelWidth * 4, 0);
        var bounds = Rect.Empty;
        for (var y = 0; y < plate.PixelHeight; y++)
            for (var x = 0; x < plate.PixelWidth; x++)
                if (pixels[(y * plate.PixelWidth + x) * 4 + 3] > 16) bounds.Union(rotation.Transform(new Point(x, y)));
        bounds.Inflate(8, 8);
        var frame = new Canvas { Width = bounds.Width, Height = bounds.Height };
        canvas.RenderTransform = rotation; Canvas.SetLeft(canvas, -bounds.Left); Canvas.SetTop(canvas, -bounds.Top);
        frame.Children.Add(canvas); return frame;
    }

    private static TextBlock Text(string text, double size, string color) => new()
    { Text = text, FontSize = size, Foreground = UiResources.Brush(color), VerticalAlignment = VerticalAlignment.Center };

    private void PaintRow(string id, bool hover)
    {
        var cell = _cells[id];
        cell.Row.Background = UiResources.Brush(_selected == id ? "#D8E2EE" : hover ? "#EEF0F3" : "#FFFFFF");
    }

    public void Update(DayStats day)
    {
        _day = day;
        var maximum = MouseCatalog.All.Select(m => day.Keys.GetValueOrDefault(m.Id)).DefaultIfEmpty(1).Max();
        foreach (var surface in _artwork.Surfaces)
        {
            var id = surface.Id; var cell = _cells[id];
            var count = day.Keys.GetValueOrDefault(id);
            var level = HeatScale.Level(count, maximum); var selected = _selected == id;
            if (_painted.TryGetValue(id, out var before) && before == (count, level, selected)) continue;
            // Texture work only happens across a heat-level boundary, never on every click/refresh.
            cell.Button.HeatTexture = surface.Texture(level);
            cell.Button.HeatLevel = level;
            cell.Button.Background = HeatScale.Colors[level];
            cell.Button.Foreground = UiResources.Brush(level >= 3 ? "#FFFFFF" : "#181B21");
            cell.Button.IsSelected = selected; cell.Swatch.Background = HeatScale.Colors[level];
            cell.Count.Text = count.ToString("N0");
            var name = $"{KeyCatalog.Get(id).Name} · {count:N0} 次点击";
            AutomationProperties.SetName(cell.Button, name);
            _painted[id] = (count, level, selected); PaintRow(id, cell.Button.IsMouseOver);
        }
    }

    internal void SelectButton(string? id)
    {
        _selected = id is not null && MouseCatalog.IsMouse(id) ? id : null;
        if (_day is not null) Update(_day);
    }
}

/// <summary>Native keyboard and automation behavior, with hit testing confined to the actual part.</summary>
public sealed class MouseRegionButton : Button
{
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(Geometry), typeof(MouseRegionButton));
    public static readonly DependencyProperty HeatTextureProperty = DependencyProperty.Register(nameof(HeatTexture), typeof(BitmapSource), typeof(MouseRegionButton));
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(MouseRegionButton), new PropertyMetadata(false));
    public static readonly DependencyProperty IsEmphasizedProperty = DependencyProperty.Register(nameof(IsEmphasized), typeof(bool), typeof(MouseRegionButton), new PropertyMetadata(false));
    public Geometry Shape { get => (Geometry)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public BitmapSource HeatTexture { get => (BitmapSource)GetValue(HeatTextureProperty); set => SetValue(HeatTextureProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsEmphasized { get => (bool)GetValue(IsEmphasizedProperty); set => SetValue(IsEmphasizedProperty, value); }
    internal int HeatLevel { get; set; }

    public MouseRegionButton() => SetResourceReference(StyleProperty, "MouseRegionButton");

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => Shape?.FillContains(parameters.HitPoint) == true
        ? new PointHitTestResult(this, parameters.HitPoint) : null;
}
