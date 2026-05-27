using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class UsageLevelTests
{
    [Theory]
    [InlineData(0, UsageLevel.Calm)]
    [InlineData(69.999, UsageLevel.Calm)]
    [InlineData(70, UsageLevel.Warn)]
    [InlineData(89.999, UsageLevel.Warn)]
    [InlineData(90, UsageLevel.Hot)]
    [InlineData(150, UsageLevel.Hot)]
    public void ForPercentMapsBoundaries(double percent, UsageLevel expected)
    {
        UsageLevelExtensions.ForPercent(percent).Should().Be(expected);
    }
}
