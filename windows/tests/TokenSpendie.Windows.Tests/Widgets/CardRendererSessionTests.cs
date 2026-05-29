extern alias widget;

using System.Text.Json;
using FluentAssertions;
using Xunit;
using CardRenderer = widget::TokenSpendie.WidgetProvider.Cards.CardRenderer;
using ModelWeekly = widget::TokenSpendie.Windows.Models.ModelWeekly;
using UsageSnapshot = widget::TokenSpendie.Windows.Models.UsageSnapshot;
using UsageWindow = widget::TokenSpendie.Windows.Models.UsageWindow;

namespace TokenSpendie.Windows.Tests.Widgets;

public class CardRendererSessionTests
{
    [Fact]
    public void RendersAdaptiveCardWithPercentAndRingImage()
    {
        var snapshot = new UsageSnapshot(
            Session: new UsageWindow(45.0, DateTimeOffset.UtcNow.AddHours(3)),
            Weekly: new UsageWindow(20.0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: DateTimeOffset.UtcNow);

        var renderer = new CardRenderer();
        var json = renderer.Render("TokenSpendie.Session", "small", snapshot);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("AdaptiveCard");
        doc.RootElement.GetProperty("version").GetString().Should().Be("1.5");

        var body = doc.RootElement.GetProperty("body");
        body.GetArrayLength().Should().BeGreaterThan(0);

        json.Should().Contain("45%");
        json.Should().Contain("data:image/png;base64,");
    }
}
