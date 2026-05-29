using System.Windows;
using FluentAssertions;
using TokenSpendie.Windows.Tray;
using Xunit;

namespace TokenSpendie.Windows.Tests.Tray;

public class TrayPositioningTests
{
    [Fact]
    public void BottomRightAnchorsLeftSoBoardSitsAgainstWorkAreaRightEdgeWithGutter()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var (left, top) = TrayPositioning.BottomRight(work, width: 520, height: 600);

        left.Should().Be(1920 - 520 - 8);   // = 1392
        top.Should().Be(1040 - 600 - 8);    //  = 432
    }

    [Fact]
    public void BottomRightClampsTopToAtLeastWorkAreaTopPlusGutter()
    {
        var work = new Rect(0, 0, 1920, 700);
        var (_, top) = TrayPositioning.BottomRight(work, width: 520, height: 1200);

        top.Should().Be(work.Top + 8);
    }

    [Fact]
    public void BottomRightUsesProvidedRectOriginNotOriginZero()
    {
        var work = new Rect(1920, 0, 1920, 1040);
        var (left, _) = TrayPositioning.BottomRight(work, width: 520, height: 600);

        left.Should().Be(3840 - 520 - 8);   // = 3312
    }
}
