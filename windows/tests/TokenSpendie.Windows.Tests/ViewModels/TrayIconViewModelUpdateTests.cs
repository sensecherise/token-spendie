using System.IO;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class TrayIconViewModelUpdateTests : System.IDisposable
{
    private readonly string _dir;

    public TrayIconViewModelUpdateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"tvmupd-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private TrayIconViewModel Make()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"));
        var providers = new IUsageProvider[] { Substitute.For<IUsageProvider>() };
        providers[0].Id.Returns(ProviderID.Claude);
        providers[0].DisplayName.Returns("Claude");
        var store = new UsageStore(providers, preferences: prefs);
        return new TrayIconViewModel(store, prefs);
    }

    [Fact]
    public void CheckForUpdatesCommandRaisesEvent()
    {
        var vm = Make();
        var fired = 0;
        vm.CheckForUpdatesRequested += (_, _) => fired++;
        vm.CheckForUpdatesCommand.Execute(null);
        fired.Should().Be(1);
    }
}
