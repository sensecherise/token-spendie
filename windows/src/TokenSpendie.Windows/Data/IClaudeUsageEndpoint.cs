using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public interface IClaudeUsageEndpoint
{
    Task<UsageSnapshot> FetchUsageAsync(string accessToken, CancellationToken ct = default);
}
