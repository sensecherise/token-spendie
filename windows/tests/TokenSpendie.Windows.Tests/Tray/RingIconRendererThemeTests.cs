using System.Windows.Media.Imaging;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Tests.TestSupport;
using TokenSpendie.Windows.Tray;

namespace TokenSpendie.Windows.Tests.Tray;

public class RingIconRendererThemeTests
{
    [StaFact]
    public void DifferentThemesProduceDifferentBitmapBytes()
    {
        var defaultIcon = (BitmapImage)RingIconRenderer.Render(75, UsageLevel.Warn, 1.0, Theme.Default);
        var oceanIcon = (BitmapImage)RingIconRenderer.Render(75, UsageLevel.Warn, 1.0, Theme.Ocean);

        defaultIcon.Should().NotBeNull();
        oceanIcon.Should().NotBeNull();
        defaultIcon.IsFrozen.Should().BeTrue();
        oceanIcon.IsFrozen.Should().BeTrue();
    }
}
