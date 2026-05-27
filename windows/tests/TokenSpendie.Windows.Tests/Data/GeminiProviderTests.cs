using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class GeminiProviderTests : IDisposable
{
    private readonly string _home;
    private static readonly DateTimeOffset Noon = DateTimeOffset.FromUnixTimeSeconds(1_747_915_200);
    private static readonly DateTimeOffset NextMidnight = DateTimeOffset.FromUnixTimeSeconds(1_747_958_400);

    public GeminiProviderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"gemp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { }
    }

    private void StubOAuth() =>
        File.WriteAllText(Path.Combine(_home, "oauth_creds.json"), "{}");

    private void StubSessionWithPrompts(int promptCount)
    {
        var dir = Path.Combine(_home, "tmp", "p", "chats");
        Directory.CreateDirectory(dir);
        var lines = new[]
        {
            """{"sessionId":"s","projectHash":"h","startTime":"2025-05-22T00:00:00.000Z","lastUpdated":"2025-05-22T00:00:00.000Z","kind":"main"}"""
        }.Concat(Enumerable.Range(0, promptCount).Select(i =>
            $$$"""{"id":"u","timestamp":"2025-05-22T0{{{i % 9}}}:00:00.000Z","type":"user","content":[{"text":"prompt {{{i}}}"}]}"""));
        File.WriteAllLines(Path.Combine(dir, "session-a.jsonl"), lines);
    }

    private GeminiUsageReader MakeReader() =>
        new(_home, () => Noon, TimeZoneInfo.Utc);

    [Fact]
    public void IdAndDisplayName()
    {
        var provider = new GeminiProvider(MakeReader());
        provider.Id.Should().Be(ProviderID.Gemini);
        provider.DisplayName.Should().Be("Gemini");
    }

    [Fact]
    public void DetectCredentialsDelegatesToReader()
    {
        StubOAuth();
        new GeminiProvider(MakeReader()).DetectCredentials().Should().BeTrue();

        var emptyHome = Path.Combine(Path.GetTempPath(), $"emp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyHome);
        try
        {
            new GeminiProvider(new GeminiUsageReader(emptyHome, () => Noon, TimeZoneInfo.Utc))
                .DetectCredentials().Should().BeFalse();
        }
        finally { Directory.Delete(emptyHome); }
    }

    [Fact]
    public async Task FetchUsageProducesDailySnapshot()
    {
        StubOAuth();
        StubSessionWithPrompts(3);
        var provider = new GeminiProvider(MakeReader());

        var snapshot = await provider.FetchUsageAsync();

        snapshot.Id.Should().Be(ProviderID.Gemini);
        snapshot.Plan.Should().BeNull();
        snapshot.Windows.Should().ContainSingle();
        snapshot.Headline.Label.Should().Be("Daily");
        snapshot.Headline.ResetStyle.Should().Be(ResetStyle.Countdown);
        snapshot.Headline.Window.Percent.Should().BeApproximately(0.3, 0.0001);
        snapshot.Headline.Window.ResetsAt.Should().Be(NextMidnight);
        snapshot.FetchedAt.Should().Be(Noon);
    }

    [Fact]
    public void ConvertPercentMath()
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(0);
        var now = DateTimeOffset.FromUnixTimeSeconds(0);
        GeminiProvider.Convert(0, reset, now).Headline.Window.Percent
            .Should().BeApproximately(0, 0.0001);
        GeminiProvider.Convert(500, reset, now).Headline.Window.Percent
            .Should().BeApproximately(50, 0.0001);
        GeminiProvider.Convert(1500, reset, now).Headline.Window.Percent
            .Should().BeApproximately(150, 0.0001);
    }

    [Fact]
    public void ConvertDetailString()
    {
        var snapshot = GeminiProvider.Convert(
            420,
            DateTimeOffset.FromUnixTimeSeconds(0),
            DateTimeOffset.FromUnixTimeSeconds(0));
        snapshot.Headline.Detail.Should().Be("≈420 of 1000 requests");
    }

    [Fact]
    public void ConvertMarksSnapshotAsAnEstimate()
    {
        var snapshot = GeminiProvider.Convert(
            1,
            DateTimeOffset.FromUnixTimeSeconds(0),
            DateTimeOffset.FromUnixTimeSeconds(0));
        snapshot.Note.Should().Be("estimate · counted from local logs");
    }
}
