using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class PreferencesStorePositionTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PreferencesStorePositionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"prefsp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "prefs.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void DefaultFloatingPanelSizeIs540x420()
    {
        var p = new PreferencesStore(_path);
        p.FloatingPanelWidth.Should().Be(540);
        p.FloatingPanelHeight.Should().Be(420);
        p.FloatingPanelLeft.Should().BeNull();
        p.FloatingPanelTop.Should().BeNull();
    }

    [Fact]
    public void FloatingPanelPositionRoundTripsToDisk()
    {
        var p1 = new PreferencesStore(_path);
        p1.FloatingPanelLeft = 200.5;
        p1.FloatingPanelTop = 300.0;
        p1.FloatingPanelWidth = 320;
        p1.FloatingPanelHeight = 240;

        var p2 = new PreferencesStore(_path);
        p2.FloatingPanelLeft.Should().Be(200.5);
        p2.FloatingPanelTop.Should().Be(300.0);
        p2.FloatingPanelWidth.Should().Be(320);
        p2.FloatingPanelHeight.Should().Be(240);
    }
}
