using System;
using FluentAssertions;
using TokenSpendie.Windows.Util;
using Xunit;

namespace TokenSpendie.Windows.Tests.Util;

public class FormattingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact] public void ResetCountdownNullReturnsEmpty() =>
        Formatting.ResetCountdown(null, Now).Should().Be("");

    [Fact] public void ResetCountdownPastReturnsResettingNow() =>
        Formatting.ResetCountdown(Now.AddSeconds(-5), Now).Should().Be("resetting now");

    [Fact] public void ResetCountdownMinutesOnly() =>
        Formatting.ResetCountdown(Now.AddMinutes(40), Now).Should().Be("resets in 40m");

    [Fact] public void ResetCountdownHoursAndMinutes() =>
        Formatting.ResetCountdown(Now.AddMinutes(2 * 60 + 47), Now).Should().Be("resets in 2h 47m");

    [Fact] public void ResetDateNullReturnsEmpty() =>
        Formatting.ResetDate(null).Should().Be("");

    [Fact] public void ResetDateFormatsAsWeekday()
    {
        var date = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero); // Monday
        Formatting.ResetDate(date).Should().Be("resets Mon, May 25");
    }

    [Fact] public void UpdatedAgoJustNow() =>
        Formatting.UpdatedAgo(Now, Now).Should().Be("updated just now");

    [Fact] public void UpdatedAgoSeconds() =>
        Formatting.UpdatedAgo(Now.AddSeconds(-10), Now).Should().Be("updated 10s ago");

    [Fact] public void UpdatedAgoMinutes() =>
        Formatting.UpdatedAgo(Now.AddMinutes(-5), Now).Should().Be("updated 5m ago");

    [Fact] public void UpdatedAgoHours() =>
        Formatting.UpdatedAgo(Now.AddHours(-2), Now).Should().Be("updated 2h ago");
}
