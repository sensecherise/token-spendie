using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class UsageStoreIntegrationTests : IDisposable
{
    private readonly string _dir;
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public UsageStoreIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"usi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static IUsageProvider Stub(ProviderID id, string name,
        Func<CancellationToken, Task<ProviderSnapshot>> fetch)
    {
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(true);
        p.FetchUsageAsync(Arg.Any<CancellationToken>()).Returns(ci => fetch((CancellationToken)ci[0]));
        return p;
    }

    private static ProviderSnapshot Snap(ProviderID id)
    {
        var headline = new LabeledWindow("Session", "5h", ResetStyle.Countdown, new UsageWindow(10, null));
        return new ProviderSnapshot(id, null, headline, new[] { headline },
            DateTimeOffset.FromUnixTimeSeconds(0));
    }

    [Fact]
    public async Task MenuBarProviderReturnsPreferenceWhenPresent()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"))
        {
            MenuBarProviderID = ProviderID.Gemini,
        };
        var store = new UsageStore(
            new[]
            {
                Stub(ProviderID.Claude, "Claude", _ => Task.FromResult(Snap(ProviderID.Claude))),
                Stub(ProviderID.Gemini, "Gemini", _ => Task.FromResult(Snap(ProviderID.Gemini))),
            },
            preferences: prefs,
            now: () => _now);

        store.MenuBarProvider().Should().BeNull("no refresh yet");
        await store.RefreshAsync();
        store.MenuBarProvider()!.Id.Should().Be(ProviderID.Gemini);
    }

    [Fact]
    public async Task MenuBarProviderFallsBackToFirstWhenPreferenceMissing()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"))
        {
            MenuBarProviderID = ProviderID.Gemini,
        };
        var store = new UsageStore(
            new[] { Stub(ProviderID.Claude, "Claude", _ => Task.FromResult(Snap(ProviderID.Claude))) },
            preferences: prefs,
            now: () => _now);
        await store.RefreshAsync();
        store.MenuBarProvider()!.Id.Should().Be(ProviderID.Claude);
    }

    [Fact]
    public async Task NetworkReconnectTriggersRefresh()
    {
        var net = Substitute.For<INetworkAvailabilityObserver>();
        var calls = 0;
        var store = new UsageStore(
            new[] { Stub(ProviderID.Claude, "Claude",
                _ => { calls++; return Task.FromResult(Snap(ProviderID.Claude)); }) },
            preferences: new PreferencesStore(Path.Combine(_dir, "prefs.json")),
            now: () => _now,
            network: net);

        await store.RefreshAsync();
        calls.Should().Be(1);

        net.Reconnected += Raise.Event<EventHandler>(net, EventArgs.Empty);
        await Task.Delay(50);

        calls.Should().Be(2);
    }
}
