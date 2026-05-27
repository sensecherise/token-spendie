using System;
using System.IO;
using System.Text;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class OAuthCredentialsParserTests
{
    [Fact]
    public void ParsesSecondsExpiry()
    {
        var json = """{"claudeAiOauth":{"accessToken":"abc","refreshToken":"ref","expiresAt":1700000000}}""";
        var creds = OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        creds.AccessToken.Should().Be("abc");
        creds.RefreshToken.Should().Be("ref");
        creds.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    }

    [Fact]
    public void ParsesMillisecondsExpiry()
    {
        var json = """{"claudeAiOauth":{"accessToken":"abc","expiresAt":1700000000000}}""";
        var creds = OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        creds.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        creds.RefreshToken.Should().BeNull();
    }

    [Fact]
    public void ToleratesExtraWindowsFields()
    {
        // From the M0 spike: scopes, subscriptionType, rateLimitTier appear on Windows.
        var json = """
        {"claudeAiOauth":{
          "accessToken":"abc","refreshToken":"ref","expiresAt":1779844385628,
          "scopes":["user:profile","user:inference"],
          "subscriptionType":"max","rateLimitTier":"some-tier-string"
        }}
        """;
        var creds = OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        creds.AccessToken.Should().Be("abc");
        creds.RefreshToken.Should().Be("ref");
    }

    [Fact]
    public void MissingAccessTokenThrowsMalformed()
    {
        var json = """{"claudeAiOauth":{"refreshToken":"ref"}}""";
        Action act = () => OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        act.Should().Throw<CredentialMalformedException>();
    }

    [Fact]
    public void EmptyAccessTokenThrowsMalformed()
    {
        var json = """{"claudeAiOauth":{"accessToken":""}}""";
        Action act = () => OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        act.Should().Throw<CredentialMalformedException>();
    }

    [Fact]
    public void GarbageThrowsMalformed()
    {
        Action act = () => OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes("not json"));
        act.Should().Throw<CredentialMalformedException>();
    }

    [Fact]
    public void IsExpiredComparesAgainstNow()
    {
        var past = new OAuthCredentials("a", null, DateTimeOffset.FromUnixTimeSeconds(100));
        past.IsExpired(DateTimeOffset.FromUnixTimeSeconds(200)).Should().BeTrue();
        past.IsExpired(DateTimeOffset.FromUnixTimeSeconds(50)).Should().BeFalse();

        var noExpiry = new OAuthCredentials("a", null, null);
        noExpiry.IsExpired(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void ParsesM0SanitizedFixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "superpowers", "findings", "fixtures",
            "claude-credentials-sanitized.json");
        fixturePath = Path.GetFullPath(fixturePath);
        File.Exists(fixturePath).Should().BeTrue($"fixture must exist at {fixturePath}");
        var bytes = File.ReadAllBytes(fixturePath);
        var creds = OAuthCredentialsParser.Parse(bytes);
        // Sanitized fixture has "<redacted>" as access token — non-empty, so parse succeeds.
        creds.AccessToken.Should().Be("<redacted>");
        creds.RefreshToken.Should().Be("<redacted>");
        // expiresAt = 1779844385628 → milliseconds → 2026-05-30 UTC.
        creds.ExpiresAt!.Value.Year.Should().Be(2026);
    }
}
