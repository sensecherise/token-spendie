using System;
using FluentAssertions;
using TokenSpendie.Windows.Tray;
using Xunit;

namespace TokenSpendie.Windows.Tests.Tray;

public class TrayIconControllerFormatTests
{
    [Fact]
    public void NullTimestampReportsNever()
    {
        TrayIconController.FormatLastChecked(null).Should().Be("Last checked: never");
    }

    [Fact]
    public void FutureOrSubMinuteReportsJustNow()
    {
        var now = DateTimeOffset.UtcNow;
        TrayIconController.FormatLastChecked(now).Should().Be("Last checked: just now");
        TrayIconController.FormatLastChecked(now.AddSeconds(30)).Should().Be("Last checked: just now");
        TrayIconController.FormatLastChecked(now - TimeSpan.FromSeconds(30)).Should().Be("Last checked: just now");
    }

    [Fact]
    public void MinutesBucketShowsMinutes()
    {
        var when = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        TrayIconController.FormatLastChecked(when).Should().Be("Last checked: 5m ago");
    }

    [Fact]
    public void HoursBucketShowsHours()
    {
        var when = DateTimeOffset.UtcNow - TimeSpan.FromHours(3);
        TrayIconController.FormatLastChecked(when).Should().Be("Last checked: 3h ago");
    }

    [Fact]
    public void DaysBucketShowsDays()
    {
        var when = DateTimeOffset.UtcNow - TimeSpan.FromDays(2);
        TrayIconController.FormatLastChecked(when).Should().Be("Last checked: 2d ago");
    }
}
