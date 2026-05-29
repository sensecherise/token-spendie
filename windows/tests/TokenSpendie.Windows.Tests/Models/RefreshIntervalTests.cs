using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class RefreshIntervalTests
{
    [Fact]
    public void DefinedValuesAreSixtyAndOneTwenty()
    {
        RefreshInterval.S60.Seconds().Should().Be(60);
        RefreshInterval.S120.Seconds().Should().Be(120);
    }

    [Fact]
    public void LabelFormatsAsHumanReadableString()
    {
        RefreshInterval.S60.Label().Should().Be("60 seconds");
        RefreshInterval.S120.Label().Should().Be("2 minutes");
    }

    [Theory]
    [InlineData(60, RefreshInterval.S60)]
    [InlineData(120, RefreshInterval.S120)]
    public void FromSecondsReturnsKnownInterval(int seconds, RefreshInterval expected)
    {
        RefreshIntervalExtensions.FromSeconds(seconds).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(180)]
    [InlineData(999999)]
    public void FromSecondsFallsBackToS60ForUnknown(int seconds)
    {
        RefreshIntervalExtensions.FromSeconds(seconds).Should().Be(RefreshInterval.S60);
    }
}
