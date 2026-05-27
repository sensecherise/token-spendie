using System;
using System.ComponentModel;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class PreferencesStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PreferencesStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"prefs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "prefs.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private PreferencesStore Make() => new(_path);

    [Fact]
    public void DefaultsAppliedWhenFileMissing()
    {
        var p = Make();
        p.ShowMenuBar.Should().BeTrue();
        p.ShowFloatingPanel.Should().BeFalse();
        p.RefreshInterval.Should().Be(RefreshInterval.S60);
        p.LaunchAtLogin.Should().BeFalse();
        p.Theme.Should().Be(Theme.Default);
        p.MenuBarProviderID.Should().Be(ProviderID.Claude);
    }

    [Fact]
    public void ChangesPersistToDisk()
    {
        var p1 = Make();
        p1.RefreshInterval = RefreshInterval.S120;
        p1.Theme = Theme.Ocean;
        p1.MenuBarProviderID = ProviderID.Gemini;
        p1.LaunchAtLogin = true;
        p1.ShowMenuBar = false;
        p1.ShowFloatingPanel = true;

        var p2 = Make();
        p2.RefreshInterval.Should().Be(RefreshInterval.S120);
        p2.Theme.Should().Be(Theme.Ocean);
        p2.MenuBarProviderID.Should().Be(ProviderID.Gemini);
        p2.LaunchAtLogin.Should().BeTrue();
        p2.ShowMenuBar.Should().BeFalse();
        p2.ShowFloatingPanel.Should().BeTrue();
    }

    [Fact]
    public void GarbageFileFallsBackToDefaults()
    {
        File.WriteAllText(_path, "not json");
        var p = Make();
        p.RefreshInterval.Should().Be(RefreshInterval.S60);
        p.Theme.Should().Be(Theme.Default);
    }

    [Fact]
    public void RaisesPropertyChangedOnSet()
    {
        var p = Make();
        var fired = new System.Collections.Generic.List<string?>();
        p.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        p.Theme = Theme.Sunset;
        p.RefreshInterval = RefreshInterval.S120;

        fired.Should().Contain(nameof(PreferencesStore.Theme));
        fired.Should().Contain(nameof(PreferencesStore.RefreshInterval));
    }

    [Fact]
    public void SettingSameValueDoesNotRaiseChange()
    {
        var p = Make();
        var fired = 0;
        p.PropertyChanged += (_, _) => fired++;
        p.Theme = p.Theme;
        fired.Should().Be(0);
    }
}
