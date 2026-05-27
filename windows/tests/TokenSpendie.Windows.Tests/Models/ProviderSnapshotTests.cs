using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ProviderSnapshotTests
{
    private static JsonSerializerOptions Options()
    {
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opts;
    }

    [Fact]
    public void ProviderIDEncodesAsCamelCaseString()
    {
        JsonSerializer.Serialize(ProviderID.Claude, Options()).Should().Be("\"claude\"");
        JsonSerializer.Serialize(ProviderID.Gemini, Options()).Should().Be("\"gemini\"");
    }

    [Fact]
    public void ProviderIDDecodesFromCamelCase()
    {
        JsonSerializer.Deserialize<ProviderID>("\"claude\"", Options()).Should().Be(ProviderID.Claude);
        JsonSerializer.Deserialize<ProviderID>("\"gemini\"", Options()).Should().Be(ProviderID.Gemini);
    }

    [Fact]
    public void ResetStyleEncodesAsCamelCase()
    {
        JsonSerializer.Serialize(ResetStyle.Countdown, Options()).Should().Be("\"countdown\"");
        JsonSerializer.Serialize(ResetStyle.Date, Options()).Should().Be("\"date\"");
    }

    [Fact]
    public void ProviderSnapshotRoundTripsWithModelWeekly()
    {
        var headline = new LabeledWindow(
            Label: "Session", Detail: "5-hour window",
            ResetStyle: ResetStyle.Countdown,
            Window: new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)));
        var weekly = new LabeledWindow(
            Label: "Weekly", Detail: "all models",
            ResetStyle: ResetStyle.Date,
            Window: new UsageWindow(31, DateTimeOffset.FromUnixTimeSeconds(200)));

        var snapshot = new ProviderSnapshot(
            Id: ProviderID.Claude, Plan: "Max",
            Headline: headline, Windows: new[] { headline, weekly },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999), Note: null);

        var json = JsonSerializer.Serialize(snapshot, Options());
        JsonSerializer.Deserialize<ProviderSnapshot>(json, Options()).Should().Be(snapshot);
    }

    [Fact]
    public void ProviderSnapshotRoundTripsWithNoteAndNullPlan()
    {
        var headline = new LabeledWindow(
            Label: "Daily", Detail: "≈3 of 1000 requests",
            ResetStyle: ResetStyle.Countdown,
            Window: new UsageWindow(0.3, DateTimeOffset.FromUnixTimeSeconds(1)));
        var snapshot = new ProviderSnapshot(
            Id: ProviderID.Gemini, Plan: null, Headline: headline,
            Windows: new[] { headline },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(0),
            Note: "estimate · counted from local logs");

        var json = JsonSerializer.Serialize(snapshot, Options());
        JsonSerializer.Deserialize<ProviderSnapshot>(json, Options()).Should().Be(snapshot);
    }
}
