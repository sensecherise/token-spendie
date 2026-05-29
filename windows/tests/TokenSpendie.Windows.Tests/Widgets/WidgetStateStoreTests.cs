extern alias widget;

using FluentAssertions;
using Xunit;
using WidgetStateStore = widget::TokenSpendie.WidgetProvider.WidgetStateStore;

namespace TokenSpendie.Windows.Tests.Widgets;

public class WidgetStateStoreTests
{
    [Fact]
    public void AddAndGetRoundtrips()
    {
        var s = new WidgetStateStore();
        s.Add("w1", "TokenSpendie.Session", "small");
        var info = s.Get("w1");
        info.Should().NotBeNull();
        info!.Kind.Should().Be("TokenSpendie.Session");
        info.Size.Should().Be("small");
    }

    [Fact]
    public void RemoveDropsState()
    {
        var s = new WidgetStateStore();
        s.Add("w2", "TokenSpendie.Full", "medium");
        s.Remove("w2");
        s.Get("w2").Should().BeNull();
    }

    [Fact]
    public void UpdateSizeReflectsInGet()
    {
        var s = new WidgetStateStore();
        s.Add("w3", "TokenSpendie.Full", "small");
        s.UpdateSize("w3", "medium");
        s.Get("w3")!.Size.Should().Be("medium");
    }
}
