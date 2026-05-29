extern alias widget;

using FluentAssertions;
using Xunit;
using CardRenderer = widget::TokenSpendie.WidgetProvider.Cards.CardRenderer;
using ModelWeekly = widget::TokenSpendie.Windows.Models.ModelWeekly;
using UsageSnapshot = widget::TokenSpendie.Windows.Models.UsageSnapshot;
using UsageWindow = widget::TokenSpendie.Windows.Models.UsageWindow;

namespace TokenSpendie.Windows.Tests.Widgets;

public class CardRendererEmptyTests
{
    [Fact]
    public void RendersNoCliDetectedWhenSnapshotIsEmpty()
    {
        var empty = new UsageSnapshot(
            Session: new UsageWindow(0, null),
            Weekly: new UsageWindow(0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: DateTimeOffset.UtcNow);

        var renderer = new CardRenderer();
        var json = renderer.Render("TokenSpendie.Session", "small", empty);

        json.Should().Contain("No CLI detected.");
        json.Should().Contain("Install Claude Code or Gemini CLI");
        json.Should().NotContain("data:image/png;base64,");
        json.Should().NotContain("%");
    }

    [Fact]
    public void FullKindAlsoRendersEmptyCardWhenSnapshotIsEmpty()
    {
        var empty = new UsageSnapshot(
            Session: new UsageWindow(0, null),
            Weekly: new UsageWindow(0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: DateTimeOffset.UtcNow);

        var renderer = new CardRenderer();
        var json = renderer.Render("TokenSpendie.Full", "medium", empty);

        json.Should().Contain("No CLI detected.");
        json.Should().NotContain("Action.OpenUrl");
    }
}
