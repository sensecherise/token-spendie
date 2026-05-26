using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

/// <summary>
/// Reads Claude Code OAuth credentials from a plain-JSON file at
/// <c>%USERPROFILE%\.claude\.credentials.json</c>. Storage location confirmed by
/// the M0 spike (see <c>docs/superpowers/findings/2026-05-26-windows-creds-spike.md</c>).
/// </summary>
public sealed class ClaudeJsonFileReader : ICredentialReader
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BackoffBetweenAttempts = TimeSpan.FromMilliseconds(50);

    private readonly string _path;

    public ClaudeJsonFileReader()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    /// <summary>Test-friendly constructor. <paramref name="userProfile"/> stands in for <c>%USERPROFILE%</c>.</summary>
    public ClaudeJsonFileReader(string userProfile)
    {
        _path = Path.Combine(userProfile, ".claude", ".credentials.json");
    }

    public bool CredentialsExist() => File.Exists(_path);

    public async Task<OAuthCredentials> LoadCredentialsAsync(CancellationToken ct = default)
    {
        Exception? lastMalformed = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            byte[] bytes;
            try
            {
                // FileShare.ReadWrite lets Claude Code rewrite the file while we read it (G9).
                using var stream = new FileStream(
                    _path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096, useAsync: true);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                bytes = ms.ToArray();
            }
            catch (FileNotFoundException) { throw new CredentialNotFoundException(); }
            catch (DirectoryNotFoundException) { throw new CredentialNotFoundException(); }
            catch (UnauthorizedAccessException ex) { throw new CredentialAccessDeniedException(ex); }
            catch (IOException ex) when (attempt < MaxAttempts - 1)
            {
                // Sharing violation or partial write — back off and retry.
                await Task.Delay(BackoffBetweenAttempts, ct).ConfigureAwait(false);
                lastMalformed = ex;
                continue;
            }

            try
            {
                return OAuthCredentialsParser.Parse(bytes);
            }
            catch (CredentialMalformedException ex) when (attempt < MaxAttempts - 1)
            {
                // Partial JSON during a concurrent rewrite — back off and retry.
                await Task.Delay(BackoffBetweenAttempts, ct).ConfigureAwait(false);
                lastMalformed = ex;
            }
        }

        throw new CredentialMalformedException(
            "credentials file remained unreadable after retries",
            lastMalformed);
    }
}
