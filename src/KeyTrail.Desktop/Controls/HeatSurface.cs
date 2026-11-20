using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyTrail.Desktop.Controls;

internal sealed class HeatSurface
{
    private readonly byte[] _source;
    private readonly byte[] _alpha;
    private BitmapSource? _texture;
    private int _textureLevel = -1;
    private readonly bool _normalizeLight;
    private readonly double _luminanceBase;
    internal string Id { get; }
    internal Int32Rect Bounds { get; }
    internal Geometry Shape { get; }
    internal long RetainedPixelBytes => _source.LongLength + _alpha.LongLength + Bounds.Width * (long)Bounds.Height * 4;

    internal HeatSurface(string id, BitmapSource image, Transform scale, string contour, bool normalizeLight = false)
    {
        Id = id;
        var outline = Geometry.Parse(contour).Clone(); outline.Transform = scale;
        var bounds = outline.Bounds;
        // Include room for the focus/selection strokes, while preserving exact image pixel alignment.
        var x = Math.Max(0, (int)Math.Floor(bounds.Left) - 5);
        var y = Math.Max(0, (int)Math.Floor(bounds.Top) - 5);
        Bounds = new Int32Rect(x, y, Math.Min(image.PixelWidth - x, (int)Math.Ceiling(bounds.Right) - x + 5),
            Math.Min(image.PixelHeight - y, (int)Math.Ceiling(bounds.Bottom) - y + 5));
        var local = outline.Clone();
        var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(new TranslateTransform(-x, -y));
        local.Transform = transforms; local.Freeze(); Shape = local;
        _source = new byte[Bounds.Width * Bounds.Height * 4];
        image.CopyPixels(Bounds, _source, Bounds.Width * 4, 0);

        // Antialiased surface coverage is generated once; the same contour supplies native hit tests.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawGeometry(Brushes.White, null, Shape);
        var mask = new RenderTargetBitmap(Bounds.Width, Bounds.Height, 96, 96, PixelFormats.Pbgra32); mask.Render(visual);
        var maskPixels = new byte[_source.Length]; mask.CopyPixels(maskPixels, Bounds.Width * 4, 0);
        _alpha = new byte[Bounds.Width * Bounds.Height];
        for (var i = 0; i < _alpha.Length; i++) _alpha[i] = (byte)(maskPixels[i * 4 + 3] * _source[i * 4 + 3] / 255);
        _normalizeLight = normalizeLight;
        double luminance = 0; var opaque = 0;
        if (normalizeLight)
            for (var i = 0; i < _alpha.Length; i++)
                if (_alpha[i] > 240) { var p = i * 4; luminance += .0722 * _source[p] + .7152 * _source[p + 1] + .2126 * _source[p + 2]; opaque++; }
        _luminanceBase = normalizeLight && opaque > 0 ? Math.Max(1, luminance / opaque) : 155;
    }

    internal BitmapSource Texture(int level)
    {
        if (_textureLevel == level) return _texture!;
        var color = ((SolidColorBrush)HeatScale.Colors[level]).Color;
        var lookup = new byte[256 * 3];
        for (var luminance = 0; luminance < 256; luminance++)
        {
            // Normalize the photographed keycap albedo so an equally used key has the same heat,
            // with bounded surface lighting that keeps small native labels readable.
            var light = _normalizeLight ? Math.Clamp(luminance / _luminanceBase, .85, 1.10) : luminance / _luminanceBase;
            static byte Channel(byte c, double light) => (byte)Math.Clamp(Math.Round(light <= 1 ? c * light : c + (255 - c) * (light - 1) * .55), 0, 255);
            lookup[luminance * 3] = Channel(color.B, light);
            lookup[luminance * 3 + 1] = Channel(color.G, light);
            lookup[luminance * 3 + 2] = Channel(color.R, light);
        }
        var pixels = new byte[_source.Length];
        for (var i = 0; i < _alpha.Length; i++)
        {
            if (_alpha[i] == 0) continue;
            var p = i * 4;
            var gray = (int)Math.Round(.0722 * _source[p] + .7152 * _source[p + 1] + .2126 * _source[p + 2]);
            pixels[p] = lookup[gray * 3]; pixels[p + 1] = lookup[gray * 3 + 1]; pixels[p + 2] = lookup[gray * 3 + 2]; pixels[p + 3] = _alpha[i];
        }
        _texture = BitmapSource.Create(Bounds.Width, Bounds.Height, 96, 96, PixelFormats.Bgra32, null, pixels, Bounds.Width * 4);
        _texture.Freeze(); _textureLevel = level;
        return _texture;
    }
}
