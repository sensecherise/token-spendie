using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public sealed class AntigravityProviderTests : IDisposable
{
    private readonly string _cachePath =
        Path.Combine(Path.GetTempPath(), $"ag-cache-{Guid.NewGuid():N}.json");

    public void Dispose() { try { File.Delete(_cachePath); } catch { } }

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-06-12T10:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private const string UserStatusBody = """
    {
      "userStatus": {
        "email": "user@example.com",
        "userTier": {"name": "Google AI Pro"},
        "planStatus": {"planInfo": {"planDisplayName": "Pro"}},
        "cascadeModelConfigData": {
          "clientModelConfigs": [
            {"label": "Claude Sonnet 4.5", "modelOrAlias": {"model": "M_CLAUDE"},
             "quotaInfo": {"remainingFraction": 0.82, "resetTime": "2026-06-12T18:00:00Z"}},
            {"label": "Gemini 3 Pro (Low)", "modelOrAlias": {"model": "M_PRO_LOW"},
             "quotaInfo": {"remainingFraction": 0.4, "resetTime": "1765562400"}},
            {"label": "No Quota Model", "modelOrAlias": {"model": "M_X"}}
          ]
        }
      }
    }
    """;

    private const string CommandConfigsBody = """
    {"clientModelConfigs": [
       {"label": "Gemini 3 Flash", "modelOrAlias": {"model": "M_FLASH"},
        "quotaInfo": {"remainingFraction": 0.95}}
    ]}
    """;

    private AntigravityProvider Provider(IAntigravityProbe probe) =>
        new(probe, new SnapshotCache(_cachePath), () => Now);

    private static AntigravityQuotaData Quota(AntigravityQuotaSource source, string body) =>
        new(source, Encoding.UTF8.GetBytes(body));

    // MARK: - Decoding

    [Fact]
    public async Task FetchDecodesUserStatusIntoWindows()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>())
            .Returns(Quota(AntigravityQuotaSource.UserStatus, UserStatusBody));

        var snapshot = await Provider(probe).FetchUsageAsync();

        snapshot.Id.Should().Be(ProviderID.Antigravity);
        snapshot.Plan.Should().Be("Google AI Pro");
        snapshot.Windows.Select(w => w.Label).Should().Equal("Claude Sonnet 4.5", "Gemini 3 Pro (Low)");
        // Headline = highest used: 1 - 0.4 = 60%.
        snapshot.Headline.Label.Should().Be("Gemini 3 Pro (Low)");
        snapshot.Headline.Window.Percent.Should().BeApproximately(60, 0.001);
        snapshot.Windows[0].Window.Percent.Should().BeApproximately(18, 0.001);
        // ISO reset on window 0; epoch-string reset on window 1.
        snapshot.Windows[0].Window.ResetsAt.Should().Be(
            DateTimeOffset.Parse("2026-06-12T18:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        snapshot.Windows[1].Window.ResetsAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1765562400));
        snapshot.Windows[0].ResetStyle.Should().Be(ResetStyle.Countdown);
        snapshot.FetchedAt.Should().Be(Now);
        snapshot.Note.Should().NotBeNull();
    }

    [Fact]
    public async Task FetchDecodesCommandModelConfigsFallback()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>())
            .Returns(Quota(AntigravityQuotaSource.CommandModelConfigs, CommandConfigsBody));

        var snapshot = await Provider(probe).FetchUsageAsync();
        snapshot.Windows.Select(w => w.Label).Should().Equal("Gemini 3 Flash");
        snapshot.Headline.Window.Percent.Should().BeApproximately(5, 0.001);
        snapshot.Plan.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"code": 16, "message": "unauthenticated"}""")]
    [InlineData("""{"userStatus": {"cascadeModelConfigData": {"clientModelConfigs": []}}}""")]
    [InlineData("not json")]
    public async Task BadPayloadThrowsBadResponse(string body)
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>())
            .Returns(Quota(AntigravityQuotaSource.UserStatus, body));

        var act = () => Provider(probe).FetchUsageAsync();
        await act.Should().ThrowAsync<ProviderBadResponseException>();
    }

    [Fact]
    public async Task FetchWithoutProcessThrowsNotRunning()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>())
            .Throws(new ProviderNotRunningException("Antigravity"));

        var act = () => Provider(probe).FetchUsageAsync();
        await act.Should().ThrowAsync<ProviderNotRunningException>();
    }

    // MARK: - Row semantics (detect = process OR cache < 7 days)

    [Fact]
    public void DetectTrueWhenProcessRuns()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.IsProcessPresent().Returns(true);
        Provider(probe).DetectCredentials().Should().BeTrue();
    }

    [Fact]
    public void DetectUsesCacheTtlWhenProcessAbsent()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.IsProcessPresent().Returns(false);
        var window = new LabeledWindow("Claude", "quota", ResetStyle.Countdown, new UsageWindow(10, null));

        // No cache → not detected.
        Provider(probe).DetectCredentials().Should().BeFalse();

        var cache = new SnapshotCache(_cachePath);

        // Fresh cache (3 days old) → detected.
        cache.Save(new ProviderSnapshot(
            ProviderID.Antigravity, null, window, new[] { window }, Now.AddDays(-3)));
        Provider(probe).DetectCredentials().Should().BeTrue();

        // Stale cache (8 days old) → not detected.
        cache.Save(new ProviderSnapshot(
            ProviderID.Antigravity, null, window, new[] { window }, Now.AddDays(-8)));
        Provider(probe).DetectCredentials().Should().BeFalse();
    }
}
