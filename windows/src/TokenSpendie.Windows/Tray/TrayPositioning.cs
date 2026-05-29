using System;
using System.Windows;

namespace TokenSpendie.Windows.Tray;

internal static class TrayPositioning
{
    private const double Gutter = 8.0;

    /// <summary>
    /// Anchor a popup at the bottom-right of <paramref name="workArea"/>, leaving a
    /// fixed gutter from the right and bottom edges. If the popup is taller than
    /// the work area, clamp the top to the work area's top edge (plus gutter)
    /// so the popup stays visible.
    /// </summary>
    public static (double Left, double Top) BottomRight(Rect workArea, double width, double height)
    {
        var left = workArea.Right - width - Gutter;
        var top  = Math.Max(workArea.Top + Gutter, workArea.Bottom - height - Gutter);
        return (left, top);
    }
}
