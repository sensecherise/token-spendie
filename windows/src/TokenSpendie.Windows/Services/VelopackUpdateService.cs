using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace TokenSpendie.Windows.Services;

public sealed class VelopackUpdateService : IUpdateService
{
    private readonly UpdateManager _manager;

    public VelopackUpdateService(string githubRepoUrl)
    {
        var source = new GithubSource(githubRepoUrl, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source);
    }

    public async Task<UpdateCheckResult> CheckAndApplyAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled) return UpdateCheckResult.NotInstalled;
        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null) return UpdateCheckResult.NoUpdate;
            await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
            return UpdateCheckResult.UpdateDownloaded;
        }
        catch (Exception)
        {
            return UpdateCheckResult.Error;
        }
    }
}
