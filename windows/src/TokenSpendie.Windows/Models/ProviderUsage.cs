namespace TokenSpendie.Windows.Models;

public sealed record ProviderUsage(ProviderID Id, string DisplayName)
{
    public LoadState State { get; set; } = LoadState.Loading;
    public ProviderSnapshot? Snapshot { get; set; }
}
