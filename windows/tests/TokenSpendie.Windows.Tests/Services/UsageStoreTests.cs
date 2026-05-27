using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class UsageStoreTests : IDisposable
{
    private readonly string _dir;
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public UsageStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"us-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private SnapshotCache Cache(ProviderID id) =>
        new(Path.Combine(_dir, $"snapshot-{id.ToString().ToLowerInvariant()}.json"));

    private UsageStore MakeStore(params IUsageProvider[] providers) =>
        new(providers, id => Cache(id), now: () => _now);

    private static ProviderSnapshot Snap(ProviderID id, double percent = 47, DateTimeOffset? fetchedAt = null)
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(percent, null));
        return new ProviderSnapshot(id, null, headline, new[] { headline },
            fetchedAt ?? DateTimeOffset.FromUnixTimeSeconds(0));
    }

    private static IUsageProvider Stub(ProviderID id, string name,
        bool detected, Func<CancellationToken, Task<ProviderSnapshot>>? fetch = null)
    {
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(detected);
        if (fetch is not null)
            p.FetchUsageAsync(Arg.Any<CancellationToken>()).Returns(ci => fetch((CancellationToken)ci[0]));
        return p;
    }

    [Fact]
    public async Task RefreshFetchesOnlyDetectedProviders()
    {
        var claude = Stub(ProviderID.Claude, "Claude", true, _ => Task.FromResult(Snap(ProviderID.Claude)));
        var gemini = Stub(ProviderID.Gemini, "Gemini", false);
        var store = MakeStore(claude, gemini);

        await store.RefreshAsync();

        store.Providers.Should().ContainSingle();
        store.Providers[0].Id.Should().Be(ProviderID.Claude);
        store.Providers[0].State.Should().Be(LoadState.Ok);
        await gemini.DidNotReceive().FetchUsageAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnauthorizedMapsToLoginExpiredErrorState()
    {
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => throw new ProviderUnauthorizedException());
        var store = MakeStore(claude);

        await store.RefreshAsync();

        store.Providers[0].State.Should().Be(LoadState.Error(UsageErrorKind.LoginExpired));
    }

    [Fact]
    public void NetworkErrorWithCachedSnapshotMarksStale()
    {
        var snap = Snap(ProviderID.Claude);
        Cache(ProviderID.Claude).Save(snap);

        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => throw new ProviderNetworkException(new System.Net.Http.HttpRequestException("offline")));
        var store = MakeStore(claude);
        store.Start();        // loads cache
        store.Providers[0].State.Should().Be(LoadState.Stale, "cache is older than 60s relative to _now");
    }

    [Fact]
    public async Task RateLimitedBacksOffNextRefresh()
    {
        var calls = 0;
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ =>
            {
                calls++;
                throw new ProviderRateLimitedException(TimeSpan.FromMinutes(5));
            });
        var store = MakeStore(claude);

        await store.RefreshAsync();
        calls.Should().Be(1);

        // Within backoff window — second refresh skips the provider.
        _now += TimeSpan.FromMinutes(1);
        await store.RefreshAsync();
        calls.Should().Be(1, "backoff was 5 minutes");

        // Past backoff — refreshes again.
        _now += TimeSpan.FromMinutes(5);
        await store.RefreshAsync();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ManualRefreshIgnoresBackoff()
    {
        var calls = 0;
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ =>
            {
                calls++;
                throw new ProviderRateLimitedException(TimeSpan.FromMinutes(5));
            });
        var store = MakeStore(claude);
        await store.RefreshAsync();
        calls.Should().Be(1);

        await store.ManualRefreshAsync();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ManualRefreshSwallowsRapidDoubleClick()
    {
        var calls = 0;
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => { calls++; return Task.FromResult(Snap(ProviderID.Claude)); });
        var store = MakeStore(claude);

        await store.ManualRefreshAsync();
        await store.ManualRefreshAsync();           // immediate second call — gated by 2s gap
        calls.Should().Be(1);

        _now += TimeSpan.FromSeconds(3);
        await store.ManualRefreshAsync();
        calls.Should().Be(2);
    }

    [Fact]
    public void StartLoadsFreshCachedSnapshotAsOk()
    {
        var snap = Snap(ProviderID.Claude, fetchedAt: _now.AddSeconds(-10));
        Cache(ProviderID.Claude).Save(snap);

        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => Task.FromResult(Snap(ProviderID.Claude, percent: 99)));
        var store = MakeStore(claude);

        store.Start();
        store.Providers.Should().ContainSingle();
        store.Providers[0].State.Should().Be(LoadState.Ok);
        store.Providers[0].Snapshot.Should().Be(snap);
    }

    [Fact]
    public async Task PropertyChangedRaisedForProviders()
    {
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => Task.FromResult(Snap(ProviderID.Claude)));
        var store = MakeStore(claude);
        var fired = new List<string?>();
        store.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await store.RefreshAsync();

        fired.Should().Contain(nameof(UsageStore.Providers));
    }
}
