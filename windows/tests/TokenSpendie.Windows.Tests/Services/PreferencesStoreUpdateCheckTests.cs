using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class PreferencesStoreUpdateCheckTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PreferencesStoreUpdateCheckTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"prefsu-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "prefs.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void LastUpdateCheckDefaultsToNull()
    {
        var p = new PreferencesStore(_path);
        p.LastUpdateCheck.Should().BeNull();
    }

    [Fact]
    public void LastUpdateCheckRoundTrips()
    {
        var when = new DateTimeOffset(2026, 5, 28, 12, 30, 0, TimeSpan.Zero);
        var p1 = new PreferencesStore(_path);
        p1.LastUpdateCheck = when;

        var p2 = new PreferencesStore(_path);
        p2.LastUpdateCheck.Should().Be(when);
    }
}
