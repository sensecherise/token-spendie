using System.Globalization;

namespace TokenSpendie.Windows.Util;

public static class Formatting
{
    public static string ResetCountdown(DateTimeOffset? date, DateTimeOffset now)
    {
        if (date is null) return "";
        var remaining = (int)(date.Value - now).TotalSeconds;
        if (remaining <= 0) return "resetting now";
        var hours = remaining / 3600;
        var minutes = (remaining % 3600) / 60;
        return hours > 0
            ? $"resets in {hours}h {minutes}m"
            : $"resets in {minutes}m";
    }

    public static string ResetDate(DateTimeOffset? date) =>
        date is null
            ? ""
            : $"resets {date.Value.ToString("ddd, MMM d", CultureInfo.InvariantCulture)}";

    public static string UpdatedAgo(DateTimeOffset date, DateTimeOffset now)
    {
        var elapsed = (int)(now - date).TotalSeconds;
        if (elapsed < 3) return "updated just now";
        if (elapsed < 60) return $"updated {elapsed}s ago";
        if (elapsed < 3600) return $"updated {elapsed / 60}m ago";
        return $"updated {elapsed / 3600}h ago";
    }
}
