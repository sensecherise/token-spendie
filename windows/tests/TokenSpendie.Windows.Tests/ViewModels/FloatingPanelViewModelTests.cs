using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class FloatingPanelViewModelTests : IDisposable
{
    private readonly string _dir;
    public FloatingPanelViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"fpvm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private PreferencesStore Prefs() => new(Path.Combine(_dir, "prefs.json"));

    [Fact]
    public void DefaultPositionIsNullSoWindowCenters()
    {
        var vm = new FloatingPanelViewModel(Prefs(), panelVm: null!);
        vm.Left.Should().BeNull();
        vm.Top.Should().BeNull();
    }

    [Fact]
    public void DefaultSizeIs540By420()
    {
        var vm = new FloatingPanelViewModel(Prefs(), panelVm: null!);
        vm.Width.Should().Be(540);
        vm.Height.Should().Be(420);
    }

    [Fact]
    public void SavePersistsPositionToPreferences()
    {
        var prefs = Prefs();
        var vm = new FloatingPanelViewModel(prefs, panelVm: null!);

        vm.Save(left: 100.5, top: 200, width: 320, height: 240);

        prefs.FloatingPanelLeft.Should().Be(100.5);
        prefs.FloatingPanelTop.Should().Be(200);
        prefs.FloatingPanelWidth.Should().Be(320);
        prefs.FloatingPanelHeight.Should().Be(240);
    }

    [Fact]
    public void LoadsExistingPositionFromPreferences()
    {
        var prefs = Prefs();
        prefs.FloatingPanelLeft = 50;
        prefs.FloatingPanelTop = 60;
        prefs.FloatingPanelWidth = 300;
        prefs.FloatingPanelHeight = 250;

        var vm = new FloatingPanelViewModel(prefs, panelVm: null!);
        vm.Left.Should().Be(50);
        vm.Top.Should().Be(60);
        vm.Width.Should().Be(300);
        vm.Height.Should().Be(250);
    }
}
