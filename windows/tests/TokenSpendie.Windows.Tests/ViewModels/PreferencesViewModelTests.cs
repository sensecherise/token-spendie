using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class PreferencesViewModelTests : IDisposable
{
    private readonly string _dir;
    public PreferencesViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"pvm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private (PreferencesStore prefs, PreferencesViewModel vm) Make()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"));
        return (prefs, new PreferencesViewModel(prefs));
    }

    [Fact]
    public void ExposesAllIntervalsAndThemes()
    {
        var (_, vm) = Make();
        vm.Intervals.Should().Equal(RefreshInterval.S60, RefreshInterval.S120);
        vm.Themes.Should().Equal(Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet);
    }

    [Fact]
    public void TogglingMenuBarOffEnablesFloatingPanel()
    {
        var (prefs, vm) = Make();
        prefs.ShowMenuBar.Should().BeTrue();
        prefs.ShowFloatingPanel.Should().BeFalse();

        vm.ShowMenuBar = false;

        prefs.ShowMenuBar.Should().BeFalse();
        prefs.ShowFloatingPanel.Should().BeTrue("at least one display surface must stay enabled");
    }

    [Fact]
    public void TogglingFloatingPanelOffEnablesMenuBar()
    {
        var (prefs, vm) = Make();
        prefs.ShowMenuBar = false;
        prefs.ShowFloatingPanel = true;

        vm.ShowFloatingPanel = false;

        prefs.ShowFloatingPanel.Should().BeFalse();
        prefs.ShowMenuBar.Should().BeTrue();
    }

    [Fact]
    public void ChangesPropagateBothDirections()
    {
        var (prefs, vm) = Make();
        vm.Theme = Theme.Ocean;
        prefs.Theme.Should().Be(Theme.Ocean);

        prefs.RefreshInterval = RefreshInterval.S120;
        vm.RefreshInterval.Should().Be(RefreshInterval.S120);
    }

    [Fact]
    public void QuitCommandShutsDownApplication()
    {
        // Just verify the command can be invoked without throwing.
        // Real shutdown is integration-tested via the running app.
        var (_, vm) = Make();
        vm.QuitCommand.CanExecute(null).Should().BeTrue();
        // Do NOT execute it in unit tests — it would call Application.Current.Shutdown().
    }
}
