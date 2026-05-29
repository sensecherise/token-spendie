extern alias widget;

using System.Text.Json;
using FluentAssertions;
using Xunit;
using CardRenderer = widget::TokenSpendie.WidgetProvider.Cards.CardRenderer;
using ModelWeekly = widget::TokenSpendie.Windows.Models.ModelWeekly;
using UsageSnapshot = widget::TokenSpendie.Windows.Models.UsageSnapshot;
using UsageWindow = widget::TokenSpendie.Windows.Models.UsageWindow;

namespace TokenSpendie.Windows.Tests.Widgets;

public class CardRendererFullTests
{
    [Fact]
    public void RendersFullCardWithSessionAndWeeklyLines()
    {
        var snapshot = new UsageSnapshot(
            Session: new UsageWindow(45.0, null),
            Weekly: new UsageWindow(20.0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: DateTimeOffset.UtcNow);

        var renderer = new CardRenderer();
        var json = renderer.Render("TokenSpendie.Full", "medium", snapshot);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("AdaptiveCard");
        json.Should().Contain("45%");
        json.Should().Contain("20%");
        json.Should().Contain("Session");
        json.Should().Contain("Weekly");
        json.Should().Contain("Action.OpenUrl");
    }
}
