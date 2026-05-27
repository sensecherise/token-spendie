using System.Windows.Media;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Tests.TestSupport;
using TokenSpendie.Windows.Tray;

namespace TokenSpendie.Windows.Tests.Tray;

public class RingIconRendererTests
{
    [StaFact]
    public void RenderProducesNonNullFrozenBitmap()
    {
        var icon = RingIconRenderer.Render(percent: 50, level: UsageLevel.Warn, dpiScale: 1.0, theme: Theme.Default);
        icon.Should().NotBeNull();
        icon!.IsFrozen.Should().BeTrue("the bitmap must be frozen to be assigned across threads");
    }

    [StaFact]
    public void RenderUsesIconPxForBitmapSize()
    {
        // At 2.0 scale → 32×32 px.
        var icon = RingIconRenderer.Render(percent: 75, level: UsageLevel.Warn, dpiScale: 2.0, theme: Theme.Default);
        ((System.Windows.Media.Imaging.BitmapSource)icon!).PixelWidth.Should().Be(32);
        ((System.Windows.Media.Imaging.BitmapSource)icon).PixelHeight.Should().Be(32);
    }

    [StaFact]
    public void RenderClampsOverHundredPercentToFullRing()
    {
        // Should not throw; should produce a valid bitmap at any input.
        RingIconRenderer.Render(percent: 150, level: UsageLevel.Hot, dpiScale: 1.0, theme: Theme.Default).Should().NotBeNull();
        RingIconRenderer.Render(percent: -5, level: UsageLevel.Calm, dpiScale: 1.0, theme: Theme.Default).Should().NotBeNull();
    }
}
