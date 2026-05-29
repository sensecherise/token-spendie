using System.Linq;
using System.Windows.Media;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Tests.TestSupport;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ThemeTests
{
    [Fact]
    public void AllThemesHaveDistinctDisplayNames()
    {
        var names = new[] { Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet }
            .Select(t => t.DisplayName())
            .ToArray();
        names.Should().OnlyHaveUniqueItems();
        names.Should().Equal("Default", "Ocean", "Sunset", "Violet");
    }

    [StaFact]
    public void ColorForReturnsAColorForEveryTier()
    {
        foreach (var theme in new[] { Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet })
        {
            foreach (var level in new[] { UsageLevel.Calm, UsageLevel.Warn, UsageLevel.Hot })
            {
                theme.ColorFor(level).Should().NotBe(default(Color),
                    $"theme {theme} level {level} should have a color");
            }
        }
    }

    [StaFact]
    public void TierColorsDifferWithinATheme()
    {
        var t = Theme.Default;
        t.ColorFor(UsageLevel.Calm).Should().NotBe(t.ColorFor(UsageLevel.Warn));
        t.ColorFor(UsageLevel.Warn).Should().NotBe(t.ColorFor(UsageLevel.Hot));
        t.ColorFor(UsageLevel.Calm).Should().NotBe(t.ColorFor(UsageLevel.Hot));
    }
}
