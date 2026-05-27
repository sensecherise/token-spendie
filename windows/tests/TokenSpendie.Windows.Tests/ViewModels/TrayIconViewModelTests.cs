using System;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Tests.TestSupport;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class TrayIconViewModelTests
{
    private static IUsageProvider Stub(ProviderID id, string name, ProviderSnapshot? snap)
    {
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(true);
        p.FetchUsageAsync(default).ReturnsForAnyArgs(Task.FromResult(snap!));
        return p;
    }

    private static ProviderSnapshot Snap(double percent)
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(percent, null));
        return new ProviderSnapshot(ProviderID.Claude, null, headline,
            new[] { headline }, DateTimeOffset.FromUnixTimeSeconds(0));
    }

    [StaFact]
    public async Task ToolTipReflectsHeadlinePercent()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(47.3)) });
        await store.RefreshAsync();
        var vm = new TrayIconViewModel(store);
        vm.ToolTipText.Should().Contain("47");
        vm.ToolTipText.Should().Contain("Claude");
    }

    [StaFact]
    public async Task IconSourceIsSetAfterRefresh()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(50)) });
        await store.RefreshAsync();
        var vm = new TrayIconViewModel(store);
        vm.IconSource.Should().NotBeNull();
    }

    [StaFact]
    public void ToolTipFallbackBeforeData()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(50)) });
        var vm = new TrayIconViewModel(store);
        vm.ToolTipText.Should().Be("Token Spendie — loading…");
    }

    [StaFact]
    public void LeftClickCommandRaisesShowPopupRequested()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(10)) });
        var vm = new TrayIconViewModel(store);
        var fired = 0;
        vm.ShowPopupRequested += (_, _) => fired++;
        vm.LeftClickCommand.Execute(null);
        fired.Should().Be(1);
    }
}
