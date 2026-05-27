using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class LoadStateTests
{
    [Fact]
    public void LoadStateValueEquality()
    {
        LoadState.Loading.Should().Be(LoadState.Loading);
        LoadState.Ok.Should().Be(LoadState.Ok);
        LoadState.Stale.Should().Be(LoadState.Stale);
        LoadState.Error(UsageErrorKind.Network).Should().Be(LoadState.Error(UsageErrorKind.Network));
        LoadState.Error(UsageErrorKind.Network).Should().NotBe(LoadState.Error(UsageErrorKind.BadResponse));
        LoadState.Loading.Should().NotBe(LoadState.Ok);
    }

    [Fact]
    public void ProviderUsageStateIsMutable()
    {
        var usage = new ProviderUsage(ProviderID.Claude, "Claude")
        {
            State = LoadState.Loading,
        };
        usage.State = LoadState.Ok;
        usage.State.Should().Be(LoadState.Ok);
    }

    [Fact]
    public void ProviderUsageDistinguishesByState()
    {
        var a = new ProviderUsage(ProviderID.Claude, "Claude") { State = LoadState.Loading };
        var b = new ProviderUsage(ProviderID.Claude, "Claude") { State = LoadState.Ok };
        a.Equals(b).Should().BeFalse();
    }
}
