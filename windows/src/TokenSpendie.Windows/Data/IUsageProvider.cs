using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public interface IUsageProvider
{
    ProviderID Id { get; }
    string DisplayName { get; }
    bool DetectCredentials();
    Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default);
}
