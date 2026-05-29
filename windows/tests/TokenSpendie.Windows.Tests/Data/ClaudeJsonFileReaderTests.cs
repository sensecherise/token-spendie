using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class ClaudeJsonFileReaderTests : IDisposable
{
    private readonly string _tempHome;
    private readonly string _credsPath;

    public ClaudeJsonFileReaderTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"tsw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempHome, ".claude"));
        _credsPath = Path.Combine(_tempHome, ".claude", ".credentials.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempHome, recursive: true); } catch { }
    }

    private ClaudeJsonFileReader Reader() => new(_tempHome);

    private void WriteValidCreds(string accessToken = "tok", long expiresAtMs = 1779844385628)
    {
        File.WriteAllText(_credsPath,
            $$$"""{"claudeAiOauth":{"accessToken":"{{{accessToken}}}","refreshToken":"r","expiresAt":{{{expiresAtMs}}}}}""");
    }

    [Fact]
    public void CredentialsExistFalseWhenFileMissing()
    {
        Reader().CredentialsExist().Should().BeFalse();
    }

    [Fact]
    public void CredentialsExistTrueWhenFilePresent()
    {
        WriteValidCreds();
        Reader().CredentialsExist().Should().BeTrue();
    }

    [Fact]
    public async Task LoadCredentialsAsyncReturnsParsed()
    {
        WriteValidCreds(accessToken: "abc");
        var creds = await Reader().LoadCredentialsAsync();
        creds.AccessToken.Should().Be("abc");
    }

    [Fact]
    public async Task LoadCredentialsAsyncThrowsNotFoundWhenAbsent()
    {
        Func<Task> act = () => Reader().LoadCredentialsAsync();
        await act.Should().ThrowAsync<CredentialNotFoundException>();
    }

    [Fact]
    public async Task LoadCredentialsAsyncThrowsMalformedOnGarbage()
    {
        File.WriteAllText(_credsPath, "not json");
        Func<Task> act = () => Reader().LoadCredentialsAsync();
        await act.Should().ThrowAsync<CredentialMalformedException>();
    }

    [Fact]
    public async Task LoadCredentialsAsyncRetriesOnConcurrentRewrite()
    {
        // First two reads see partial JSON; third sees a complete file.
        // The reader retries up to 3 times with a 50ms backoff.
        File.WriteAllText(_credsPath, """{"claudeAiOauth":{"accessTok"""); // truncated
        var readsBeforeComplete = 0;
        var fixerTask = Task.Run(async () =>
        {
            await Task.Delay(60);            // after first retry
            File.WriteAllText(_credsPath, """{"claudeAiOauth":{"accessToken":"final"}}""");
            Interlocked.Increment(ref readsBeforeComplete);
        });

        var creds = await Reader().LoadCredentialsAsync();
        await fixerTask;
        creds.AccessToken.Should().Be("final");
    }

    [Fact]
    public async Task LoadCredentialsAsyncGivesUpAfterThreeAttempts()
    {
        File.WriteAllText(_credsPath, "{garbage");
        var reader = Reader();
        Func<Task> act = () => reader.LoadCredentialsAsync();
        await act.Should().ThrowAsync<CredentialMalformedException>();
    }
}
