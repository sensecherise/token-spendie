using System.Threading;
using System.Threading.Tasks;

namespace TokenSpendie.Windows.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAndApplyAsync(CancellationToken ct = default);
}
