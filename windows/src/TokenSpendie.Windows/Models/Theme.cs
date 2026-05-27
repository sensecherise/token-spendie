using System.Windows.Media;

namespace TokenSpendie.Windows.Models;

public enum Theme { Default, Ocean, Sunset, Violet }

public static class ThemeExtensions
{
    public static string DisplayName(this Theme theme) => theme switch
    {
        Theme.Default => "Default",
        Theme.Ocean => "Ocean",
        Theme.Sunset => "Sunset",
        Theme.Violet => "Violet",
        _ => theme.ToString(),
    };

    public static Color ColorFor(this Theme theme, UsageLevel level)
    {
        var (calm, warn, hot) = TierColors(theme);
        return level switch
        {
            UsageLevel.Calm => calm,
            UsageLevel.Warn => warn,
            UsageLevel.Hot => hot,
            _ => calm,
        };
    }

    private static (Color Calm, Color Warn, Color Hot) TierColors(Theme theme) => theme switch
    {
        Theme.Default => (
            Color.FromRgb(0x5F, 0xB8, 0x78),
            Color.FromRgb(0xE0, 0xA2, 0x3F),
            Color.FromRgb(0xD9, 0x53, 0x4F)),
        Theme.Ocean => (
            Color.FromRgb(0x35, 0xC0, 0xA6),
            Color.FromRgb(0xF0, 0xBD, 0x5A),
            Color.FromRgb(0xEF, 0x6F, 0x6C)),
        Theme.Sunset => (
            Color.FromRgb(0xF0, 0xA6, 0x5E),
            Color.FromRgb(0xEC, 0x7A, 0x55),
            Color.FromRgb(0xD9, 0x4B, 0x6E)),
        Theme.Violet => (
            Color.FromRgb(0x6F, 0x8F, 0xD6),
            Color.FromRgb(0xA9, 0x74, 0xD8),
            Color.FromRgb(0xD9, 0x5F, 0x9A)),
        _ => (Color.FromRgb(0x5F, 0xB8, 0x78),
              Color.FromRgb(0xE0, 0xA2, 0x3F),
              Color.FromRgb(0xD9, 0x53, 0x4F)),
    };
}
