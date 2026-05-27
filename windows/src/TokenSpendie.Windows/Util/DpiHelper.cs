namespace TokenSpendie.Windows.Util;

/// <summary>Pixel ↔ DIP conversion + tray icon size for a given DPI scale.</summary>
public static class DpiHelper
{
    /// <summary>96 DPI = 1.0 scale = 1 DIP per pixel.</summary>
    public const double BaselineDpi = 96.0;

    public static double PixelsToDips(double pixels, double scale) => pixels / scale;
    public static double DipsToPixels(double dips, double scale) => dips * scale;

    /// <summary>The tray icon size in physical pixels for the given DPI scale.
    /// 16 at 100%, 20 at 125%, 24 at 150%, 32 at 200%.</summary>
    public static int IconPx(double scale) => (int)System.Math.Ceiling(16 * scale);
}
