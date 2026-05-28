using System.Threading.Tasks;
using FluentAssertions;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class VelopackUpdateServiceTests
{
    static VelopackUpdateServiceTests()
    {
        // Velopack's UpdateManager constructor requires a VelopackLocator,
        // which is only set after VelopackApp.Build().Run(). Call it once
        // for the test process. Run() with no args is a no-op.
        Velopack.VelopackApp.Build().Run();
    }

    [Fact]
    public async Task UninstalledAppReportsNotInstalled()
    {
        var svc = new VelopackUpdateService("https://github.com/sensecherise/token-spendie");
        var result = await svc.CheckAndApplyAsync();
        result.Should().Be(UpdateCheckResult.NotInstalled);
    }
}
