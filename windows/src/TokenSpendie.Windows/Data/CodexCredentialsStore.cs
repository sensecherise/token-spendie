using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

/// <summary>Codex CLI's OAuth token set, as stored in <c>auth.json</c>.</summary>
public sealed record CodexCredentials(
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    string? AccountId,
    DateTimeOffset? LastRefresh)
{
    /// <summary>Codex CLI's own policy: refresh when <c>last_refresh</c> is
    /// older than 8 days (or missing).</summary>
    public bool NeedsRefresh(DateTimeOffset now) =>
        LastRefresh is not { } stamp || (now - stamp) > TimeSpan.FromDays(8);
}

/// <summary>
/// Reads and (after a token refresh) rewrites Codex CLI's <c>auth.json</c>
/// (<c>%CODEX_HOME%\auth.json</c> or <c>%USERPROFILE%\.codex\auth.json</c>).
/// Write-back preserves every field this app does not own — the file belongs
/// to Codex CLI; we only rotate the token set, exactly like the CLI itself.
/// Known limitation: a Codex CLI refresh landing between our read and write
/// is clobbered (lost-update race); the window is milliseconds.
/// </summary>
public sealed class CodexCredentialsStore
{
    public string FilePath { get; }

    public CodexCredentialsStore(string path) => FilePath = path;

    public CodexCredentialsStore()
        : this(DefaultPath(
            Environment.GetEnvironmentVariable("CODEX_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))) { }

    public static string DefaultPath(string? codexHome, string userProfile) =>
        string.IsNullOrEmpty(codexHome)
            ? Path.Combine(userProfile, ".codex", "auth.json")
            : Path.Combine(codexHome, "auth.json");

    /// <summary>True when <c>auth.json</c> holds a non-empty OAuth access
    /// token. API-key-only files are not detected. Cheap, never prompts.</summary>
    public bool Detect()
    {
        try { return !string.IsNullOrEmpty(Load().AccessToken); }
        catch { return false; }
    }

    /// <summary>Loads the token set. Missing/malformed state maps to
    /// <see cref="ProviderReauthRequiredException"/> — by the time this runs
    /// the provider was detected, so a broken file means "log in again".</summary>
    public CodexCredentials Load()
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath));
        }
        catch (Exception)
        {
            throw new ProviderReauthRequiredException("Codex");
        }
        var tokens = root?["tokens"];
        var accessToken = tokens?["access_token"]?.GetValue<string>();
        var refreshToken = tokens?["refresh_token"]?.GetValue<string>();
        if (accessToken is null || refreshToken is null)
            throw new ProviderReauthRequiredException("Codex");

        DateTimeOffset? lastRefresh = null;
        if (root!["last_refresh"]?.GetValue<string>() is { } stamp &&
            DateTimeOffset.TryParse(
                stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            lastRefresh = parsed;
        }
        return new CodexCredentials(
            accessToken, refreshToken,
            tokens?["id_token"]?.GetValue<string>(),
            tokens?["account_id"]?.GetValue<string>(),
            lastRefresh);
    }

    /// <summary>Atomically rewrites <c>auth.json</c> with the refreshed token
    /// set, keeping all fields we do not own.</summary>
    /// ACLs are inherited from Codex CLI's own file; intentionally not set
    /// here (the macOS 0600 chmod has no needed Windows equivalent).
    public void Save(CodexCredentials creds, DateTimeOffset refreshedAt)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception)
        {
            root = new JsonObject();
        }
        var tokens = root["tokens"] as JsonObject ?? new JsonObject();
        tokens["access_token"] = creds.AccessToken;
        tokens["refresh_token"] = creds.RefreshToken;
        if (creds.IdToken is { } idToken) tokens["id_token"] = idToken;
        if (creds.AccountId is { } accountId) tokens["account_id"] = accountId;
        root["tokens"] = tokens;
        // Invariant culture is mandatory: the host may run a non-Gregorian
        // calendar (e.g. the Thai Buddhist calendar renders 2026 as 2569),
        // which would corrupt the stamp Codex CLI and we both read back.
        root["last_refresh"] = refreshedAt.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var tmp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
