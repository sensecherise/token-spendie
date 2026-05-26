using System;
using System.Text.Json;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class UsageSnapshotTests
{
    private static JsonSerializerOptions Options() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void UsageWindowRoundTrips()
    {
        var resetsAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var window = new UsageWindow(Percent: 47.5, ResetsAt: resetsAt);

        var json = JsonSerializer.Serialize(window, Options());
        var deserialized = JsonSerializer.Deserialize<UsageWindow>(json, Options());

        deserialized.Should().Be(window);
    }

    [Fact]
    public void UsageWindowAllowsNullResetsAt()
    {
        var window = new UsageWindow(Percent: 10, ResetsAt: null);
        var json = JsonSerializer.Serialize(window, Options());
        JsonSerializer.Deserialize<UsageWindow>(json, Options()).Should().Be(window);
    }

    [Fact]
    public void UsageSnapshotRoundTrips()
    {
        var snapshot = new UsageSnapshot(
            Session: new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)),
            Weekly: new UsageWindow(31, DateTimeOffset.FromUnixTimeSeconds(200)),
            ModelWeeklies: new[]
            {
                new ModelWeekly("Opus", new UsageWindow(62, null)),
            },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999));

        var json = JsonSerializer.Serialize(snapshot, Options());
        JsonSerializer.Deserialize<UsageSnapshot>(json, Options()).Should().Be(snapshot);
    }

    [Fact]
    public void UsageSnapshotValueEqualityComparesByContents()
    {
        var a = new UsageSnapshot(
            new UsageWindow(1, null), new UsageWindow(2, null),
            Array.Empty<ModelWeekly>(), DateTimeOffset.FromUnixTimeSeconds(0));
        var b = a with { };
        a.Should().Be(b);
    }
}
