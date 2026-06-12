using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ProviderIDTests
{
    [Fact]
    public void CodexCaseExists()
    {
        System.Enum.GetNames<ProviderID>().Should().Contain("Codex");
        UsageErrorKind.CodexReauthRequired.Should().BeDefined();
    }
}
