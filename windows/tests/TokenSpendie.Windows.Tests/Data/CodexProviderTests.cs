using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public sealed class CodexProviderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "codex-provider-" + Guid.NewGuid().ToString("N"));

    public CodexProviderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // The provider clock: one day after the fresh fixture's last_refresh.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-11T00:00:00Z");

    private const string FullUsageBody = """
    {
      "plan_type": "pro",
      "rate_limit": {
        "primary_window":   {"used_percent": 15, "reset_at": 1735401600, "limit_window_seconds": 18000},
        "secondary_window": {"used_percent": 5,  "reset_at": 1735920000, "limit_window_seconds": 604800}
      },
      "credits": {"has_credits": true, "unlimited": false, "balance": 150.0}
    }
    """;

    /// <summary>Writes a real auth.json and returns a store over it. Default
    /// stamp is fresh (one day before <see cref="Now"/>).</summary>
    private CodexCredentialsStore FreshStore(string lastRefresh = "2026-06-10T00:00:00Z")
    {
        var path = Path.Combine(_dir, "auth.json");
        File.WriteAllText(path, $$"""
        {"tokens": {"access_token": "tok-1", "refresh_token": "ref-1", "account_id": "acct-1"},
         "last_refresh": "{{lastRefresh}}"}
        """);
        return new CodexCredentialsStore(path);
    }

    private static CodexUsage FullUsage() => new(
        "pro",
        new CodexWindow(15, DateTimeOffset.FromUnixTimeSeconds(1735401600), 18000),
        new CodexWindow(5, DateTimeOffset.FromUnixTimeSeconds(1735920000), 604800));

    // MARK: - Convert / mapping

    [Fact]
    public void ConvertMapsWindowsPlanAndHeadline()
    {
        var snapshot = CodexProvider.Convert(FullUsage(), Now);

        snapshot.Id.Should().Be(ProviderID.Codex);
        snapshot.Plan.Should().Be("Pro");
        snapshot.Headline.Label.Should().Be("Session · 5h");
        snapshot.Headline.Window.Percent.Should().BeApproximately(15, 0.001);
        snapshot.Headline.ResetStyle.Should().Be(ResetStyle.Countdown);
        snapshot.Windows.Select(w => w.Label).Should().Equal("Session · 5h", "Weekly");
        snapshot.Windows[1].Window.Percent.Should().BeApproximately(5, 0.001);
        snapshot.Windows[1].ResetStyle.Should().Be(ResetStyle.Date);
        snapshot.Windows[1].Window.ResetsAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1735920000));
        snapshot.FetchedAt.Should().Be(Now);
    }

    [Fact]
    public void ConvertFallsBackToSecondaryHeadline()
    {
        var usage = new CodexUsage(
            "plus", null,
            new CodexWindow(40, DateTimeOffset.FromUnixTimeSeconds(1735920000), 604800));

        var snapshot = CodexProvider.Convert(usage, Now);
        snapshot.Headline.Label.Should().Be("Weekly");
        snapshot.Windows.Should().HaveCount(1);
        snapshot.Plan.Should().Be("Plus");
    }

    [Fact]
    public void ConvertDerivesLabelsFromDurations()
    {
        var usage = new CodexUsage(
            null,
            new CodexWindow(1, null, 21600),
            new CodexWindow(2, null, 14 * 86400));

        var snapshot = CodexProvider.Convert(usage, Now);
        snapshot.Windows.Select(w => w.Label).Should().Equal("Session · 6h", "14-day");
        snapshot.Plan.Should().BeNull();
    }

    [Fact]
    public void ConvertThrowsWhenNoWindows()
    {
        var usage = new CodexUsage("pro", null, null);
        var act = () => CodexProvider.Convert(usage, Now);
        act.Should().Throw<ProviderBadResponseException>();
    }

    // MARK: - Decode

    [Fact]
    public void DecodeParsesFullPayload()
    {
        var usage = CodexHttpClient.Decode(FullUsageBody);

        usage.PlanType.Should().Be("pro");
        usage.Primary!.UsedPercent.Should().BeApproximately(15, 0.001);
        usage.Primary.ResetsAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1735401600));
        usage.Primary.WindowSeconds.Should().Be(18000);
        usage.Secondary!.UsedPercent.Should().BeApproximately(5, 0.001);
        usage.Secondary.WindowSeconds.Should().Be(604800);
    }

    [Fact]
    public void DecodeToleratesMissingWindows()
    {
        var usage = CodexHttpClient.Decode("""{"plan_type": "pro"}""");
        usage.PlanType.Should().Be("pro");
        usage.Primary.Should().BeNull();
        usage.Secondary.Should().BeNull();
    }

    // MARK: - Fetch / refresh orchestration

    [Fact]
    public async Task FetchSkipsRefreshWhenStampFresh()
    {
        var refresher = Substitute.For<ICodexTokenRefresher>();
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FullUsage());

        var provider = new CodexProvider(FreshStore(), refresher, endpoint, () => Now);
        var snapshot = await provider.FetchUsageAsync();

        snapshot.Plan.Should().Be("Pro");
        await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    public async Task FetchRefreshesFirstWhenStampStale()
    {
        var refresher = Substitute.For<ICodexTokenRefresher>();
        refresher.RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>())
            .Returns(new CodexCredentials("tok-2", "ref-2", "id-2", "acct-1", null));
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FullUsage());

        var store = FreshStore(lastRefresh: "2026-05-01T00:00:00Z"); // stale
        var provider = new CodexProvider(store, refresher, endpoint, () => Now);
        await provider.FetchUsageAsync();

        await refresher.Received(1).RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>());
        var saved = store.Load();
        saved.RefreshToken.Should().Be("ref-2");
        saved.AccessToken.Should().Be("tok-2");
        saved.LastRefresh.Should().Be(Now);
    }

    [Fact]
    public async Task FetchOn401RefreshesOnceAndRetries()
    {
        var refresher = Substitute.For<ICodexTokenRefresher>();
        refresher.RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>())
            .Returns(new CodexCredentials("tok-2", "ref-1", null, "acct-1", null));
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new ProviderUnauthorizedException(),
                _ => Task.FromResult(FullUsage()));

        var provider = new CodexProvider(FreshStore(), refresher, endpoint, () => Now);
        var snapshot = await provider.FetchUsageAsync();

        snapshot.Plan.Should().Be("Pro");
        await endpoint.Received(2)
            .FetchUsageAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await refresher.Received(1).RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchSecond401ThrowsReauthRequired()
    {
        var refresher = Substitute.For<ICodexTokenRefresher>();
        refresher.RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>())
            .Returns(new CodexCredentials("tok-2", "ref-1", null, "acct-1", null));
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new ProviderUnauthorizedException());

        var provider = new CodexProvider(FreshStore(), refresher, endpoint, () => Now);
        var act = () => provider.FetchUsageAsync();
        await act.Should().ThrowAsync<ProviderReauthRequiredException>();
    }

    [Fact]
    public void DetectDelegatesToStore()
    {
        var refresher = Substitute.For<ICodexTokenRefresher>();
        var endpoint = Substitute.For<ICodexUsageEndpoint>();

        new CodexProvider(FreshStore(), refresher, endpoint, () => Now)
            .DetectCredentials().Should().BeTrue();

        var missing = new CodexCredentialsStore(Path.Combine(_dir, "nope.json"));
        new CodexProvider(missing, refresher, endpoint, () => Now)
            .DetectCredentials().Should().BeFalse();
    }

    // MARK: - CodexHttpClient.RefreshAsync over a stubbed transport

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public StubHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }

    private static CodexHttpClient HttpClientWith(HttpStatusCode status, string body)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        return new CodexHttpClient(new HttpClient(new StubHandler(response)));
    }

    [Fact]
    public async Task HttpRefreshKeepsOldRefreshTokenWhenResponseOmitsIt()
    {
        var client = HttpClientWith(HttpStatusCode.OK, """{"access_token": "tok-2"}""");
        var creds = new CodexCredentials("tok-1", "ref-1", "id-1", "acct-1", null);

        var refreshed = await client.RefreshAsync(creds);
        refreshed.AccessToken.Should().Be("tok-2");
        refreshed.RefreshToken.Should().Be("ref-1");
        refreshed.IdToken.Should().Be("id-1");
    }

    [Fact]
    public async Task HttpRefreshRejectionThrowsReauthRequired()
    {
        var client = HttpClientWith(HttpStatusCode.Unauthorized, "");
        var creds = new CodexCredentials("tok-1", "ref-1", null, null, null);

        var act = () => client.RefreshAsync(creds);
        await act.Should().ThrowAsync<ProviderReauthRequiredException>();
    }
}
