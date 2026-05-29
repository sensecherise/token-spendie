extern alias widget;

using FluentAssertions;
using TokenSpendie.Windows.Tests.TestSupport;
using RingPngRenderer = widget::TokenSpendie.WidgetProvider.Rendering.RingPngRenderer;
using UsageLevel = widget::TokenSpendie.Windows.Models.UsageLevel;

namespace TokenSpendie.Windows.Tests.Widgets;

public class RingPngRendererTests
{
    [StaFact]
    public void EmitsNonEmptyBase64Png()
    {
        var b64 = RingPngRenderer.RenderBase64(50, UsageLevel.Warn);
        b64.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(b64);
        bytes.Length.Should().BeGreaterThan(100);
        // PNG magic header: 89 50 4E 47
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be(0x50);
        bytes[2].Should().Be(0x4E);
        bytes[3].Should().Be(0x47);
    }
}
