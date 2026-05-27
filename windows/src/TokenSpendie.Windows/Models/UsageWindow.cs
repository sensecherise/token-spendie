namespace TokenSpendie.Windows.Models;

/// <summary>A single rate-limit window (session or weekly).</summary>
public record UsageWindow(double Percent, DateTimeOffset? ResetsAt);
