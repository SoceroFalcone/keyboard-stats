using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyTrail.Desktop.Controls;

/// <summary>The user's no-numpad keyboard: 88 physical caps (87 reported keys plus Fn).</summary>
internal sealed class KeyboardArtwork
{
    internal BitmapSource Plate { get; }
    internal IReadOnlyList<HeatSurface> Surfaces { get; }
    internal KeyboardArtwork()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/KeyTrail;component/Assets/Keyboard/keyboard-neutral.png"))!.Stream;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 1280; image.StreamSource = stream; image.EndInit(); image.Freeze(); Plate = image;
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); pixels.Freeze();
        var scale = new ScaleTransform(image.PixelWidth / 2172d, image.PixelHeight / 724d); scale.Freeze();
        var surfaces = new List<HeatSurface>();
        void Row(string[] ids, double top, double bottom, (double Left, double Right)[] faces)
        {
            if (ids.Length != faces.Length) throw new InvalidOperationException("Keyboard faces and physical keys do not match.");
            for (var i = 0; i < ids.Length; i++)
            {
                var (x, right) = faces[i]; var y = top; var b = bottom;
                // Measured molded top faces in the product plate, rather than an invented regular grid.
                var contour = string.Create(CultureInfo.InvariantCulture,
                    $"M{x+10},{y} L{right-10},{y} Q{right},{y} {right},{y+10} L{right-1},{b-12} Q{right-1},{b-3} {right-12},{b-1} Q{(x+right)/2},{b+3} {x+12},{b-1} Q{x},{b-3} {x},{b-12} L{x},{y+10} Q{x},{y} {x+10},{y} Z");
                surfaces.Add(new HeatSurface(ids[i], pixels, scale, contour, normalizeLight: true));
            }
        }
        Row(new[] { "Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "VolumeMute", "PrintScreen", "ScrollLock", "Pause" }, 46, 119,
            new[] { (87d,160d), (241d,314d), (351d,424d), (461d,534d), (571d,643d), (724d,797d), (834d,907d), (944d,1017d), (1054d,1127d), (1203d,1276d), (1312d,1385d), (1421d,1494d), (1531d,1604d), (1643d,1720d), (1800d,1872d), (1909d,1981d), (2019d,2092d) });
        Row(new[] { "Backquote", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0", "Minus", "Equal", "Backspace", "Insert", "Home", "PageUp" }, 175, 243,
            new[] { (87d,161d), (197d,271d), (306d,381d), (416d,490d), (526d,600d), (635d,709d), (745d,819d), (854d,929d), (964d,1038d), (1075d,1148d), (1184d,1258d), (1294d,1368d), (1403d,1477d), (1515d,1723d), (1800d,1872d), (1909d,1982d), (2019d,2092d) });
        Row(new[] { "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "BracketLeft", "BracketRight", "Backslash", "Delete", "End", "PageDown" }, 277, 345,
            new[] { (87d,220d), (257d,330d), (368d,441d), (478d,551d), (587d,660d), (697d,770d), (807d,880d), (916d,989d), (1026d,1099d), (1136d,1209d), (1245d,1319d), (1356d,1429d), (1466d,1540d), (1578d,1723d), (1800d,1872d), (1909d,1981d), (2019d,2091d) });
        Row(new[] { "CapsLock", "A", "S", "D", "F", "G", "H", "J", "K", "L", "Semicolon", "Quote", "Enter" }, 379, 448,
            new[] { (87d,249d), (287d,361d), (398d,471d), (508d,581d), (617d,691d), (727d,800d), (836d,910d), (947d,1020d), (1056d,1129d), (1166d,1239d), (1275d,1349d), (1386d,1460d), (1498d,1723d) });
        Row(new[] { "LShift", "Z", "X", "C", "V", "B", "N", "M", "Comma", "Period", "Slash", "RShift", "Up" }, 480, 548,
            new[] { (87d,307d), (345d,418d), (455d,528d), (565d,638d), (675d,748d), (785d,857d), (894d,967d), (1004d,1077d), (1114d,1186d), (1224d,1297d), (1334d,1407d), (1446d,1723d), (1909d,1981d) });
        Row(new[] { "LControl", "LWin", "LAlt", "Space", "RAlt", "Fn", "Menu", "RControl", "Left", "Down", "Right" }, 582, 652,
            new[] { (87d,191d), (229d,329d), (368d,470d), (510d,1159d), (1197d,1300d), (1339d,1441d), (1479d,1581d), (1620d,1723d), (1800d,1871d), (1909d,1980d), (2019d,2090d) });
        Surfaces = surfaces;
    }
}
