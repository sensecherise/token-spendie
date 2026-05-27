using System.IO;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class TrayIconViewModelMenuTests : System.IDisposable
{
    private readonly string _dir;
    public TrayIconViewModelMenuTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"tvmm-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private (UsageStore store, PreferencesStore prefs, TrayIconViewModel vm) Make()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"));
        var providers = new IUsageProvider[] { Substitute.For<IUsageProvider>() };
        providers[0].Id.Returns(ProviderID.Claude);
        providers[0].DisplayName.Returns("Claude");
        var store = new UsageStore(providers, preferences: prefs);
        return (store, prefs, new TrayIconViewModel(store, prefs));
    }

    [Fact]
    public void OpenPreferencesCommandRaisesEvent()
    {
        var (_, _, vm) = Make();
        var fired = 0;
        vm.OpenPreferencesRequested += (_, _) => fired++;
        vm.OpenPreferencesCommand.Execute(null);
        fired.Should().Be(1);
    }

    [Fact]
    public void OpenAboutCommandRaisesEvent()
    {
        var (_, _, vm) = Make();
        var fired = 0;
        vm.OpenAboutRequested += (_, _) => fired++;
        vm.OpenAboutCommand.Execute(null);
        fired.Should().Be(1);
    }

    [Fact]
    public void ToggleLaunchAtLoginFlipsPreference()
    {
        var (_, prefs, vm) = Make();
        prefs.LaunchAtLogin.Should().BeFalse();
        vm.ToggleLaunchAtLoginCommand.Execute(null);
        prefs.LaunchAtLogin.Should().BeTrue();
        vm.ToggleLaunchAtLoginCommand.Execute(null);
        prefs.LaunchAtLogin.Should().BeFalse();
    }

    [Fact]
    public void IsLaunchAtLoginReflectsPreference()
    {
        var (_, prefs, vm) = Make();
        prefs.LaunchAtLogin = true;
        vm.IsLaunchAtLogin.Should().BeTrue();
        prefs.LaunchAtLogin = false;
        vm.IsLaunchAtLogin.Should().BeFalse();
    }
}
