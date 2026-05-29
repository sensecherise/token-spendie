using FluentAssertions;
using TokenSpendie.Windows.Util;
using Xunit;

namespace TokenSpendie.Windows.Tests.Util;

public class DpiHelperTests
{
    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 1.5, 67)]   // ceiling(100/1.5)? — see impl note; pixels->DIPs rounds down via int conversion
    [InlineData(100, 2.0, 50)]
    public void PixelsToDipsDivides(double pixels, double scale, double expected)
    {
        DpiHelper.PixelsToDips(pixels, scale).Should().BeApproximately(expected, 1.0);
    }

    [Theory]
    [InlineData(50, 1.0, 50)]
    [InlineData(50, 1.5, 75)]
    [InlineData(50, 2.0, 100)]
    public void DipsToPixelsMultiplies(double dips, double scale, double expected)
    {
        DpiHelper.DipsToPixels(dips, scale).Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void IconPxRoundsUpToInt()
    {
        DpiHelper.IconPx(1.0).Should().Be(16);
        DpiHelper.IconPx(1.25).Should().Be(20);
        DpiHelper.IconPx(1.5).Should().Be(24);
        DpiHelper.IconPx(2.0).Should().Be(32);
    }
}
