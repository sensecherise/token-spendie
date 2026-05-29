using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class UsageNotifierTests : IDisposable
{
    private readonly string _dir;
    private readonly string _statePath;
    private readonly IToastSender _toasts = Substitute.For<IToastSender>();

    public UsageNotifierTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"un-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _statePath = Path.Combine(_dir, "notifier-state.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private UsageNotifier Make() => new(_toasts, _statePath);

    private static ProviderUsage Usage(ProviderID id, string name,
        double sessionPercent, double weeklyPercent,
        DateTimeOffset? sessionResets = null, DateTimeOffset? weeklyResets = null)
    {
        var session = new LabeledWindow("Session", "5-hour window", ResetStyle.Countdown,
            new UsageWindow(sessionPercent, sessionResets));
        var weekly = new LabeledWindow("Weekly", "all models", ResetStyle.Date,
            new UsageWindow(weeklyPercent, weeklyResets));
        var snap = new ProviderSnapshot(id, null, session, new[] { session, weekly },
            DateTimeOffset.FromUnixTimeSeconds(0));
        return new ProviderUsage(id, name) { State = LoadState.Ok, Snapshot = snap };
    }

    [Fact]
    public void FiresWhenSessionCrossesFifty()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains("claude.session.50")));
    }

    [Fact]
    public void DoesNotFireUnderFifty()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 49, weeklyPercent: 10) });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void FiresAllCrossedThresholdsOnTheSameCheck()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 95, weeklyPercent: 10) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".50")));
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".70")));
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".90")));
        _toasts.DidNotReceive().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".99")));
    }

    [Fact]
    public void EachThresholdFiresAtMostOncePerWindow()
    {
        var notifier = Make();
        var u = Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10);
        notifier.Check(new[] { u });
        _toasts.ClearReceivedCalls();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 65, weeklyPercent: 10) });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void WindowRolloverClearsFiredSet()
    {
        var oldReset = DateTimeOffset.FromUnixTimeSeconds(1000);
        var newReset = DateTimeOffset.FromUnixTimeSeconds(2000);
        var notifier = Make();

        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10, sessionResets: oldReset) });
        _toasts.ClearReceivedCalls();

        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10, sessionResets: newReset) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains("claude.session.50")));
    }

    [Fact]
    public void StatePersistsAcrossInstances()
    {
        var n1 = Make();
        n1.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10) });
        _toasts.ClearReceivedCalls();

        var n2 = Make();
        n2.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 60, weeklyPercent: 10) });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void IgnoresProvidersWithoutSnapshot()
    {
        var notifier = Make();
        var noData = new ProviderUsage(ProviderID.Claude, "Claude") { State = LoadState.Loading };
        notifier.Check(new[] { noData });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void SessionAndWeeklyTablesAreDistinct()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 0, weeklyPercent: 55) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains("claude.weekly.50")));
    }
}
