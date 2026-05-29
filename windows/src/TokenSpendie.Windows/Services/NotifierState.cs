namespace TokenSpendie.Windows.Services;

internal sealed class NotifierState
{
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<double>>
        FiredThresholds { get; set; } = new();
    public System.Collections.Generic.Dictionary<string, System.DateTimeOffset>
        LastResetDates { get; set; } = new();
}
