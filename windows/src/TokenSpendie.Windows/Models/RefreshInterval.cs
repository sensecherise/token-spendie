namespace TokenSpendie.Windows.Models;

/// <summary>Polling intervals offered in preferences. 60s is the floor —
/// the usage endpoint rate-limits aggressively.</summary>
public enum RefreshInterval
{
    S60 = 60,
    S120 = 120,
}

public static class RefreshIntervalExtensions
{
    public static int Seconds(this RefreshInterval interval) => (int)interval;

    public static System.TimeSpan AsTimeSpan(this RefreshInterval interval) =>
        System.TimeSpan.FromSeconds(interval.Seconds());

    public static string Label(this RefreshInterval interval) => interval switch
    {
        RefreshInterval.S60 => "60 seconds",
        RefreshInterval.S120 => "2 minutes",
        _ => $"{(int)interval} seconds",
    };

    public static RefreshInterval FromSeconds(int seconds) => seconds switch
    {
        60 => RefreshInterval.S60,
        120 => RefreshInterval.S120,
        _ => RefreshInterval.S60,
    };
}
