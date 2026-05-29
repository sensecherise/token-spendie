using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.WidgetProvider.Rendering;

/// <summary>
/// Emits a flat PNG of the usage ring. Mirrors the tray RingIconRenderer's
/// geometry but skips the ICO wrap — the widget Adaptive Card embeds the raw
/// PNG via a data: URI.
/// </summary>
public static class RingPngRenderer
{
    private const int Size = 128;
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    static RingPngRenderer() { TrackBrush.Freeze(); }

    public static string RenderBase64(double percent, UsageLevel level)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var center = new Point(Size / 2.0, Size / 2.0);
            var stroke = 16.0;
            var radius = (Size - stroke) / 2.0;

            dc.DrawEllipse(null, new Pen(TrackBrush, stroke), center, radius, radius);

            var fraction = Math.Clamp(percent / 100.0, 0, 1);
            if (fraction > 0)
            {
                var startPoint = new Point(center.X, center.Y - radius);
                var endPoint = PointOnCircle(center, radius, -90 + 360 * fraction);
                var arc = new ArcSegment(
                    endPoint, new System.Windows.Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: fraction > 0.5,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true);

                var figure = new PathFigure { StartPoint = startPoint };
                figure.Segments.Add(arc);
                var brush = new SolidColorBrush(ColorFor(level));
                brush.Freeze();
                var pen = new Pen(brush, stroke)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                dc.DrawGeometry(null, pen, new PathGeometry(new[] { figure }));
            }
        }

        var rtb = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    // Inlined default-tier palette so we don't take a dependency on Theme.cs
    // (which the widget MSIX deliberately excludes — see csproj).
    private static Color ColorFor(UsageLevel level) => level switch
    {
        UsageLevel.Calm => Color.FromRgb(0x5F, 0xB8, 0x78),
        UsageLevel.Warn => Color.FromRgb(0xE0, 0xA2, 0x3F),
        UsageLevel.Hot => Color.FromRgb(0xD9, 0x53, 0x4F),
        _ => Color.FromRgb(0x5F, 0xB8, 0x78),
    };

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var angleRad = angleDeg * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(angleRad),
                         center.Y + radius * Math.Sin(angleRad));
    }
}
