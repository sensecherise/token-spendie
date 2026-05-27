using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Util;

namespace TokenSpendie.Windows.Tray;

/// <summary>
/// Renders a usage-ring tray icon: a faint track plus a coloured arc that sweeps
/// clockwise from 12 o'clock by `percent`%. Always returns a frozen
/// <see cref="BitmapImage"/> (backed by a temp PNG file) safe to assign across
/// threads and accepted by H.NotifyIcon.Wpf 2.1.4 as an <c>IconSource</c>.
/// </summary>
public static class RingIconRenderer
{
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private static readonly Brush CalmBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0xB8, 0x78));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA2, 0x3F));
    private static readonly Brush HotBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F));

    /// <summary>Serializes concurrent <see cref="Render"/> calls so the temp ICO file write
    /// and BitmapImage init don't race when two callers target the same per-process+px file.</summary>
    private static readonly object RenderLock = new();

    static RingIconRenderer()
    {
        TrackBrush.Freeze();
        CalmBrush.Freeze();
        WarnBrush.Freeze();
        HotBrush.Freeze();
    }

    public static ImageSource Render(double percent, UsageLevel level, double dpiScale)
    {
        lock (RenderLock)
        {
            return RenderCore(percent, level, dpiScale);
        }
    }

    private static ImageSource RenderCore(double percent, UsageLevel level, double dpiScale)
    {
        var px = DpiHelper.IconPx(dpiScale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var center = new Point(px / 2.0, px / 2.0);
            var stroke = System.Math.Max(2.0, px / 8.0);
            var radius = (px - stroke) / 2.0;

            dc.DrawEllipse(null, new Pen(TrackBrush, stroke), center, radius, radius);

            var fraction = System.Math.Clamp(percent / 100.0, 0, 1);
            if (fraction > 0)
            {
                var startPoint = new Point(center.X, center.Y - radius);
                var endPoint = PointOnCircle(center, radius, -90 + 360 * fraction);
                var arc = new ArcSegment(
                    endPoint, new Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: fraction > 0.5,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true);

                var figure = new PathFigure { StartPoint = startPoint };
                figure.Segments.Add(arc);
                var pen = new Pen(BrushFor(level), stroke)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                dc.DrawGeometry(null, pen, new PathGeometry(new[] { figure }));
            }
        }

        var rtb = new RenderTargetBitmap(
            px, px, DpiHelper.BaselineDpi * dpiScale, DpiHelper.BaselineDpi * dpiScale,
            PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        // H.NotifyIcon.Wpf 2.1.4 only accepts BitmapImage (with a UriSource) or BitmapFrame
        // (treated as a URI via ToString). RenderTargetBitmap triggers NotImplementedException
        // and BitmapFrame created from a stream gives an empty ToString → UriFormatException.
        // Encode to PNG then wrap in a minimal ICO container so System.Drawing.Icon can parse
        // it. Modern Windows decodes PNG-payload ICOs without re-encoding the bitmap.

        // Encode the RTB as PNG.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        byte[] pngBytes;
        using (var pngStream = new MemoryStream())
        {
            encoder.Save(pngStream);
            pngBytes = pngStream.ToArray();
        }

        // Wrap the PNG in a minimal ICO container so System.Drawing.Icon can parse it.
        var icoBytes = WrapPngInIco(pngBytes, px);

        // Write to a per-process / per-size temp file. Reusing one filename per (process, px)
        // keeps %TEMP% from accumulating thousands of icons over a long-running session.
        // We open with FileShare.Read so concurrent renders during DPI changes don't lock
        // the file while H.NotifyIcon is still reading it.
        var tmpPath = Path.Combine(Path.GetTempPath(),
            $"tokspendie_{System.Environment.ProcessId}_{px}.ico");
        File.WriteAllBytes(tmpPath, icoBytes);

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;   // re-read each time
        bmp.UriSource = new Uri(tmpPath, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Wraps a PNG byte array in a minimal single-frame ICO container.
    /// Modern Windows decodes PNG-payload ICOs without re-encoding the bitmap.</summary>
    private static byte[] WrapPngInIco(byte[] png, int sizePx)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // ICONDIR
        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type = icon
        w.Write((ushort)1);             // image count

        // ICONDIRENTRY
        w.Write((byte)(sizePx >= 256 ? 0 : sizePx));   // width (0 means 256)
        w.Write((byte)(sizePx >= 256 ? 0 : sizePx));   // height
        w.Write((byte)0);                // color count
        w.Write((byte)0);                // reserved
        w.Write((ushort)1);              // planes
        w.Write((ushort)32);             // bits per pixel
        w.Write((uint)png.Length);       // bytes in resource
        w.Write((uint)22);              // image data offset (6 + 16)

        // PNG payload
        w.Write(png);
        return ms.ToArray();
    }

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var angleRad = angleDeg * System.Math.PI / 180.0;
        return new Point(center.X + radius * System.Math.Cos(angleRad),
                         center.Y + radius * System.Math.Sin(angleRad));
    }

    private static Brush BrushFor(UsageLevel level) => level switch
    {
        UsageLevel.Calm => CalmBrush,
        UsageLevel.Warn => WarnBrush,
        UsageLevel.Hot => HotBrush,
        _ => CalmBrush,
    };
}
