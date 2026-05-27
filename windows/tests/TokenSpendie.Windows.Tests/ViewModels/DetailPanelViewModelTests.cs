using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class DetailPanelViewModelTests
{
    private static IUsageProvider Stub(ProviderID id, string name, double percent)
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(percent, DateTimeOffset.FromUnixTimeSeconds(100)));
        var snap = new ProviderSnapshot(id, null, headline, new[] { headline },
            DateTimeOffset.FromUnixTimeSeconds(0));
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(true);
        p.FetchUsageAsync(default).ReturnsForAnyArgs(Task.FromResult(snap));
        return p;
    }

    [Fact]
    public async Task RowsReflectStoreProvidersInOrder()
    {
        var store = new UsageStore(new[]
        {
            Stub(ProviderID.Claude, "Claude", 47),
            Stub(ProviderID.Gemini, "Gemini", 3),
        });
        await store.RefreshAsync();

        var vm = new DetailPanelViewModel(store);
        vm.Rows.Select(r => r.DisplayName).Should().Equal("Claude", "Gemini");
        vm.Rows[0].HeadlinePercent.Should().BeApproximately(47, 0.001);
        vm.Rows[1].HeadlinePercent.Should().BeApproximately(3, 0.001);
    }

    [Fact]
    public async Task RowExposesLevelAndCountdown()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", 95) });
        await store.RefreshAsync();
        var vm = new DetailPanelViewModel(store);
        vm.Rows[0].Level.Should().Be(UsageLevel.Hot);
        vm.Rows[0].HeadlineDetail.Should().Be("5-hour window");
    }

    [Fact]
    public async Task RowsUpdateWhenStoreRefreshes()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", 10) });
        var vm = new DetailPanelViewModel(store);
        vm.Rows.Should().BeEmpty();
        await store.RefreshAsync();
        vm.Rows.Should().ContainSingle();
    }
}
