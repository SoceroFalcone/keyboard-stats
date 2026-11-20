using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyTrail.Desktop.Controls;

/// <summary>One decoded product plate and five cropped data surfaces, owned by the dashboard.</summary>
internal sealed class MouseArtwork
{
    internal BitmapSource Plate { get; }
    internal IReadOnlyList<HeatSurface> Surfaces { get; }

    internal MouseArtwork()
    {
        // Stream loading avoids WPF's URI image cache retaining the plate after dashboard retirement.
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/KeyTrail;component/Assets/Mouse/mouse-viper-neutral.png"))!.Stream;
        var image = new BitmapImage(); image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 640;
        image.StreamSource = stream; image.EndInit(); image.Freeze();
        Plate = image;
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); pixels.Freeze();
        var scale = new ScaleTransform(image.PixelWidth / 1536d, image.PixelHeight / 1024d); scale.Freeze();

        // Image-grounded orientation: nose upper-left, palm heel lower-right. Looking forward
        // along that axis, both thumb buttons and MouseLeft are on the physical LEFT flank.
        // X2 is nearer the nose; X1 is nearer the heel. This changes artwork, never stored IDs.
        Surfaces = new[]
        {
            new HeatSurface("MouseLeft", pixels, scale,
                "M390,126 Q394,124 401,129 L486,184 C516,197 551,238 586,264 Q595,274 609,266 L637,253 L744,360 Q749,366 742,372 L582,476 Q554,494 534,480 C460,421 359,331 287,267 Q278,260 284,247 C311,204 351,160 390,126 Z"),
            new HeatSurface("MouseRight", pixels, scale,
                "M461,89 C515,78 588,62 640,52 Q651,50 666,60 C753,112 840,192 902,276 Q911,288 900,295 L761,358 Q754,361 749,355 L640,251 L656,240 Q665,234 654,223 C624,183 603,159 574,154 L459,103 Q451,97 461,89 Z"),
            new HeatSurface("MouseMiddle", pixels, scale,
                "M499,184 C512,172 535,158 552,157 C576,153 594,165 613,187 C626,201 639,220 646,238 L631,253 L606,267 Q599,272 592,263 C563,226 536,198 506,191 Q496,189 499,184 Z"),
            new HeatSurface("MouseX1", pixels, scale,
                "M526,539 Q531,534 536,541 L610,645 Q617,655 607,660 Q595,668 583,658 L520,575 Q510,561 516,550 Z"),
            new HeatSurface("MouseX2", pixels, scale,
                "M423,421 Q428,422 436,430 L512,509 Q519,516 513,526 L505,539 Q500,545 494,537 L424,457 Q413,444 416,431 Q416,423 423,421 Z")
        };
    }
}
