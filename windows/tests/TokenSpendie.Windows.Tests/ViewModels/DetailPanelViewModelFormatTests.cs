using System;
using FluentAssertions;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class DetailPanelViewModelFormatTests
{
    [Fact]
    public void NullTimestampReportsNoData()
    {
        DetailPanelViewModel.FormatLastRefreshed(null).Should().Be("No data yet");
    }

    [Fact]
    public void FutureOrSubMinuteReportsJustNow()
    {
        var now = DateTimeOffset.UtcNow;
        DetailPanelViewModel.FormatLastRefreshed(now).Should().Be("Refreshed just now");
        DetailPanelViewModel.FormatLastRefreshed(now.AddSeconds(15)).Should().Be("Refreshed just now");
        DetailPanelViewModel.FormatLastRefreshed(now - TimeSpan.FromSeconds(20)).Should().Be("Refreshed just now");
    }

    [Fact]
    public void MinutesBucketShowsMinutes()
    {
        var when = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(7);
        DetailPanelViewModel.FormatLastRefreshed(when).Should().Be("Refreshed 7m ago");
    }

    [Fact]
    public void HoursBucketShowsHours()
    {
        var when = DateTimeOffset.UtcNow - TimeSpan.FromHours(4);
        DetailPanelViewModel.FormatLastRefreshed(when).Should().Be("Refreshed 4h ago");
    }

    [Fact]
    public void DaysBucketShowsDays()
    {
        var when = DateTimeOffset.UtcNow - TimeSpan.FromDays(3);
        DetailPanelViewModel.FormatLastRefreshed(when).Should().Be("Refreshed 3d ago");
    }
}
