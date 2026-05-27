using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class SnapshotCacheTests : IDisposable
{
    private readonly string _dir;
    public SnapshotCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"sc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private SnapshotCache Cache(ProviderID id) =>
        new(Path.Combine(_dir, $"snapshot-{id.ToString().ToLowerInvariant()}.json"));

    private static ProviderSnapshot SampleSnapshot()
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)));
        return new ProviderSnapshot(
            Id: ProviderID.Claude, Plan: null,
            Headline: headline, Windows: new[] { headline },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999));
    }

    [Fact]
    public void LoadReturnsNullWhenFileMissing()
    {
        Cache(ProviderID.Claude).Load().Should().BeNull();
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var cache = Cache(ProviderID.Claude);
        var snap = SampleSnapshot();
        cache.Save(snap);
        cache.Load().Should().Be(snap);
    }

    [Fact]
    public void LoadReturnsNullWhenJsonIsGarbage()
    {
        var cache = Cache(ProviderID.Claude);
        File.WriteAllText(cache.FileUrl, "not json");
        cache.Load().Should().BeNull();
    }

    [Fact]
    public void SaveCreatesParentDirectory()
    {
        var nestedDir = Path.Combine(_dir, "nested", "deep");
        var cache = new SnapshotCache(Path.Combine(nestedDir, "snapshot-claude.json"));
        cache.Save(SampleSnapshot());
        File.Exists(cache.FileUrl).Should().BeTrue();
    }
}
