using System.Linq;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public sealed class AntigravityProbeTests
{
    // MARK: - Process matching

    [Fact]
    public void MatchesIdeLanguageServerWithCsrfToken()
    {
        var processes = new[]
        {
            (Pid: 312, CommandLine: @"C:\Windows\System32\svchost.exe"),
            (Pid: 845, CommandLine: @"C:\Apps\Antigravity\resources\app\extensions\antigravity\bin\language_server.exe --csrf_token abc123 --app_data_dir C:\Users\x\.antigravity --extension_server_port 42100"),
        };
        var match = AntigravityProbe.FirstMatch(processes);
        match.Should().NotBeNull();
        match!.Value.Pid.Should().Be(845);
        match.Value.CsrfToken.Should().Be("abc123");
        match.Value.ExtensionPort.Should().Be(42100);
    }

    [Fact]
    public void SkipsIdeServerWithoutCsrfToken()
    {
        var processes = new[]
        {
            (Pid: 845, CommandLine: @"C:\apps\antigravity\bin\language_server.exe --app_data_dir antigravity"),
        };
        AntigravityProbe.FirstMatch(processes).Should().BeNull();
    }

    [Fact]
    public void MatchesAgyCliWithEmptyCsrfToken()
    {
        var processes = new[]
        {
            (Pid: 77, CommandLine: @"C:\Users\cherise\.local\bin\agy.exe chat"),
        };
        var match = AntigravityProbe.FirstMatch(processes);
        match.Should().NotBeNull();
        match!.Value.Pid.Should().Be(77);
        match.Value.CsrfToken.Should().Be("");
        match.Value.ExtensionPort.Should().BeNull();
    }

    [Fact]
    public void MatchesAntigravityCliPathSegmentAsCli()
    {
        // The CLI dir hosts a language_server binary too — must classify as
        // CLI (no CSRF requirement), not as a tokenless IDE to skip.
        var processes = new[]
        {
            (Pid: 91, CommandLine: @"C:\Users\x\.gemini\antigravity-cli\bin\language_server.exe --port 1"),
        };
        var match = AntigravityProbe.FirstMatch(processes);
        match.Should().NotBeNull();
        match!.Value.Pid.Should().Be(91);
        match.Value.CsrfToken.Should().Be("");
    }

    [Fact]
    public void RejectsLookalikes()
    {
        var processes = new[]
        {
            (Pid: 11, CommandLine: @"C:\bin\vim.exe antigravity-notes.md"),
            (Pid: 12, CommandLine: @"C:\bin\biology.exe --mode agy"),
            (Pid: 13, CommandLine: @"C:\other\language_server.exe --csrf_token zzz"),
        };
        AntigravityProbe.FirstMatch(processes).Should().BeNull();
    }

    // MARK: - Endpoint candidates

    [Fact]
    public void CandidateOrderHttpsPortsThenHttpExtensionPort()
    {
        var endpoints = AntigravityProbe.CandidateEndpoints(
            new[] { 42100, 42101 }, extensionPort: 42100, csrfToken: "t");
        endpoints.Select(e => $"{e.Scheme}:{e.Port}")
            .Should().Equal("https:42100", "https:42101", "http:42100");
        endpoints[0].CsrfToken.Should().Be("t");
    }

    // MARK: - Localhost-only TLS policy

    [Fact]
    public void TrustPolicyOnlyAcceptsLocalhost()
    {
        AntigravityProbe.ShouldTrustHost("127.0.0.1").Should().BeTrue();
        AntigravityProbe.ShouldTrustHost("LOCALHOST").Should().BeTrue();
        AntigravityProbe.ShouldTrustHost("::1").Should().BeTrue();
        AntigravityProbe.ShouldTrustHost("example.com").Should().BeFalse();
        AntigravityProbe.ShouldTrustHost("127.0.0.1.evil.com").Should().BeFalse();
    }
}
