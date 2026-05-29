using System;
using System.Linq;
using System.Text;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class UsageDecoderTests
{
    private static readonly DateTimeOffset FetchedAt =
        DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void DecodesPercentUtilization()
    {
        var json = """
        {
          "five_hour":      {"utilization": 44.0, "resets_at": "2026-05-21T10:20:00.431249+00:00"},
          "seven_day":      {"utilization": 8.0,  "resets_at": "2026-05-26T08:00:00.431273+00:00"},
          "seven_day_opus": {"utilization": 91.0, "resets_at": "2026-05-26T08:00:00.431280+00:00"}
        }
        """;
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        snapshot.Session.Percent.Should().BeApproximately(44, 0.001);
        snapshot.Weekly.Percent.Should().BeApproximately(8, 0.001);
        snapshot.ModelWeeklies.Should().ContainSingle();
        snapshot.ModelWeeklies[0].Model.Should().Be("Opus");
        snapshot.ModelWeeklies[0].Window.Percent.Should().BeApproximately(91, 0.001);
        snapshot.FetchedAt.Should().Be(FetchedAt);
    }

    [Fact]
    public void ParsesMicrosecondResetTime()
    {
        var json = """{"five_hour":{"utilization":1,"resets_at":"2026-05-21T10:20:00.431249+00:00"},"seven_day":{"utilization":1}}""";
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        var resetsAt = snapshot.Session.ResetsAt!.Value.ToUniversalTime();
        resetsAt.Year.Should().Be(2026);
        resetsAt.Month.Should().Be(5);
        resetsAt.Day.Should().Be(21);
        resetsAt.Hour.Should().Be(10);
        resetsAt.Minute.Should().Be(20);
    }

    [Fact]
    public void NullWindowIsOmitted()
    {
        var json = """{"five_hour":{"utilization":5},"seven_day":{"utilization":6},"seven_day_opus":null,"seven_day_sonnet":{"utilization":7}}""";
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        snapshot.ModelWeeklies.Select(m => m.Model).Should().Equal("Sonnet");
    }

    [Fact]
    public void DecodesOpusAndSonnetWeekly()
    {
        var json = """
        {"five_hour":{"utilization":1},"seven_day":{"utilization":2},"seven_day_opus":{"utilization":3},"seven_day_sonnet":{"utilization":4}}
        """;
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        snapshot.ModelWeeklies.Select(m => m.Model).Should().Equal("Opus", "Sonnet");
    }

    [Fact]
    public void MissingRequiredWindowThrowsBadResponse()
    {
        var json = """{"five_hour": {"utilization": 5}}""";
        Action act = () => UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        act.Should().Throw<ProviderBadResponseException>();
    }

    [Fact]
    public void GarbageThrowsBadResponse()
    {
        Action act = () => UsageDecoder.Decode(Encoding.UTF8.GetBytes("nonsense"), FetchedAt);
        act.Should().Throw<ProviderBadResponseException>();
    }
}
