using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public sealed class CodexCredentialsStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "codex-store-" + Guid.NewGuid().ToString("N"));

    public CodexCredentialsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string FullAuthJson = """
    {
      "OPENAI_API_KEY": null,
      "tokens": {
        "id_token": "id.jwt",
        "access_token": "access.jwt",
        "refresh_token": "refresh-1",
        "account_id": "account-123"
      },
      "last_refresh": "2026-06-01T00:00:00Z",
      "future_field": {"keep": true}
    }
    """;

    private CodexCredentialsStore Write(string json)
    {
        var path = Path.Combine(_dir, "auth.json");
        File.WriteAllText(path, json);
        return new CodexCredentialsStore(path);
    }

    [Fact]
    public void LoadParsesTokens()
    {
        var creds = Write(FullAuthJson).Load();
        creds.AccessToken.Should().Be("access.jwt");
        creds.RefreshToken.Should().Be("refresh-1");
        creds.AccountId.Should().Be("account-123");
        creds.LastRefresh.Should().Be(DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
    }

    [Fact]
    public void DetectTrueOnlyWithNonEmptyAccessToken()
    {
        Write(FullAuthJson).Detect().Should().BeTrue();
        Write("""{"OPENAI_API_KEY": "sk-x"}""").Detect().Should().BeFalse();
        Write("""{"tokens": {"access_token": ""}}""").Detect().Should().BeFalse();
        new CodexCredentialsStore(Path.Combine(_dir, "nope.json")).Detect().Should().BeFalse();
    }

    [Fact]
    public void LoadMissingOrMalformedThrowsReauthRequired()
    {
        var missing = new CodexCredentialsStore(Path.Combine(_dir, "nope.json"));
        missing.Invoking(s => s.Load()).Should().Throw<ProviderReauthRequiredException>();
        Write("not json").Invoking(s => s.Load()).Should().Throw<ProviderReauthRequiredException>();
    }

    [Fact]
    public void NeedsRefreshAfterEightDays()
    {
        var creds = Write(FullAuthJson).Load();
        var stamp = creds.LastRefresh!.Value;
        creds.NeedsRefresh(stamp.AddDays(7)).Should().BeFalse();
        creds.NeedsRefresh(stamp.AddDays(8)).Should().BeFalse("exactly 8 days is not OLDER than 8 days");
        creds.NeedsRefresh(stamp.AddDays(9)).Should().BeTrue();
        (creds with { LastRefresh = null }).NeedsRefresh(DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void SavePreservesUnknownFieldsAndUpdatesTokens()
    {
        var store = Write(FullAuthJson);
        var updated = new CodexCredentials("access-2", "refresh-2", "id-2", "account-123", null);
        store.Save(updated, DateTimeOffset.FromUnixTimeSeconds(1_780_000_000));

        var reloaded = store.Load();
        reloaded.AccessToken.Should().Be("access-2");
        reloaded.RefreshToken.Should().Be("refresh-2");
        reloaded.LastRefresh.Should().NotBeNull();

        using var doc = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        doc.RootElement.TryGetProperty("future_field", out _)
            .Should().BeTrue("unknown top-level fields must survive");
        doc.RootElement.GetProperty("OPENAI_API_KEY").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void DefaultPathHonorsCodexHome()
    {
        CodexCredentialsStore.DefaultPath(codexHome: @"C:\custom", userProfile: @"C:\Users\x")
            .Should().Be(Path.Combine(@"C:\custom", "auth.json"));
        CodexCredentialsStore.DefaultPath(codexHome: null, userProfile: @"C:\Users\x")
            .Should().Be(Path.Combine(@"C:\Users\x", ".codex", "auth.json"));
    }
}
