using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

/// <summary>A single rate-limit window from the <c>wham/usage</c> payload,
/// before label derivation.</summary>
public sealed record CodexWindow(double UsedPercent, DateTimeOffset? ResetsAt, int? WindowSeconds);

/// <summary>The decoded <c>wham/usage</c> body: the plan tier plus the two
/// optional rate-limit windows. Either window may be absent.</summary>
public sealed record CodexUsage(string? PlanType, CodexWindow? Primary, CodexWindow? Secondary);

/// <summary>Fetches the Codex <c>wham/usage</c> payload for a bearer token.</summary>
public interface ICodexUsageEndpoint
{
    Task<CodexUsage> FetchUsageAsync(string accessToken, string? accountId, CancellationToken ct = default);
}

/// <summary>Exchanges a (rotating) refresh token for a fresh token set.</summary>
public interface ICodexTokenRefresher
{
    Task<CodexCredentials> RefreshAsync(CodexCredentials creds, CancellationToken ct = default);
}

/// <summary>
/// The <see cref="IUsageProvider"/> for OpenAI Codex. Reads the CLI's OAuth
/// tokens from <c>auth.json</c>, refreshes them when stale (Codex CLI's own
/// 8-day policy) or on a 401, writes refreshed tokens back (refresh tokens
/// rotate — discarding the new one would invalidate the CLI's login), and
/// calls the <c>wham/usage</c> endpoint Codex itself uses.
/// </summary>
public sealed class CodexProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Codex;
    public string DisplayName => "Codex";

    private readonly CodexCredentialsStore _store;
    private readonly ICodexTokenRefresher _refresher;
    private readonly ICodexUsageEndpoint _endpoint;
    private readonly Func<DateTimeOffset> _now;

    public CodexProvider(
        CodexCredentialsStore store,
        ICodexTokenRefresher refresher,
        ICodexUsageEndpoint endpoint,
        Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _refresher = refresher;
        _endpoint = endpoint;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Production wiring: one shared <see cref="CodexHttpClient"/>
    /// serves as both refresher and usage endpoint.</summary>
    public CodexProvider()
    {
        _store = new CodexCredentialsStore();
        var http = new CodexHttpClient();
        _refresher = http;
        _endpoint = http;
        _now = () => DateTimeOffset.UtcNow;
    }

    public bool DetectCredentials() => _store.Detect();

    public async Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var creds = _store.Load();
        if (creds.NeedsRefresh(_now()))
            creds = await RefreshAndSaveAsync(creds, ct).ConfigureAwait(false);

        try
        {
            var usage = await _endpoint.FetchUsageAsync(creds.AccessToken, creds.AccountId, ct)
                .ConfigureAwait(false);
            return Convert(usage, _now());
        }
        catch (ProviderUnauthorizedException)
        {
            // 401/403 from wham/usage: rotate the token once and retry. A
            // second rejection means the refresh token itself is dead.
            creds = await RefreshAndSaveAsync(creds, ct).ConfigureAwait(false);
            try
            {
                var usage = await _endpoint.FetchUsageAsync(creds.AccessToken, creds.AccountId, ct)
                    .ConfigureAwait(false);
                return Convert(usage, _now());
            }
            catch (ProviderUnauthorizedException)
            {
                throw new ProviderReauthRequiredException("Codex");
            }
        }
    }

    /// <summary>One refresh round-trip + write-back. The refreshed set stamps
    /// <c>LastRefresh = now</c> so the 8-day policy resets.</summary>
    private async Task<CodexCredentials> RefreshAndSaveAsync(CodexCredentials creds, CancellationToken ct)
    {
        var refreshed = (await _refresher.RefreshAsync(creds, ct).ConfigureAwait(false))
            with { LastRefresh = _now() };
        _store.Save(refreshed, _now());
        return refreshed;
    }

    /// <summary>Maps the decoded <c>wham/usage</c> body to a
    /// <see cref="ProviderSnapshot"/>. <c>primary_window</c> (≈5 h) is the
    /// headline; either window may be absent; neither present → bad response.</summary>
    public static ProviderSnapshot Convert(CodexUsage usage, DateTimeOffset fetchedAt)
    {
        var windows = new List<LabeledWindow>();
        if (usage.Primary is { } p)
        {
            var hours = Math.Max(1, ((p.WindowSeconds ?? 18000) + 1800) / 3600);
            windows.Add(new LabeledWindow(
                $"Session · {hours}h", $"{hours}-hour window",
                ResetStyle.Countdown, new UsageWindow(p.UsedPercent, p.ResetsAt)));
        }
        if (usage.Secondary is { } s)
        {
            var days = Math.Max(1, ((s.WindowSeconds ?? 604800) + 43200) / 86400);
            windows.Add(new LabeledWindow(
                days == 7 ? "Weekly" : $"{days}-day", $"{days}-day window",
                ResetStyle.Date, new UsageWindow(s.UsedPercent, s.ResetsAt)));
        }
        if (windows.Count == 0)
            throw new ProviderBadResponseException("no rate-limit windows in wham/usage payload");

        var plan = PlanLabel(usage.PlanType);
        return new ProviderSnapshot(
            Id: ProviderID.Codex, Plan: plan,
            Headline: windows[0], Windows: windows,
            FetchedAt: fetchedAt);
    }

    /// <summary>Empty/null plan → null; otherwise <c>snake_case</c> → Title
    /// Case (e.g. <c>free_workspace</c> → "Free Workspace").</summary>
    private static string? PlanLabel(string? planType)
    {
        if (string.IsNullOrEmpty(planType)) return null;
        var parts = planType.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0
                ? part
                : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant());
        return string.Join(' ', parts);
    }
}

/// <summary>
/// Live HTTP transport for Codex: the <c>wham/usage</c> read endpoint and the
/// OAuth refresh endpoint. Never logs token values.
/// </summary>
public sealed class CodexHttpClient : ICodexUsageEndpoint, ICodexTokenRefresher
{
    private static readonly Uri UsageUrl = new("https://chatgpt.com/backend-api/wham/usage");
    private static readonly Uri RefreshUrl = new("https://auth.openai.com/oauth/token");
    /// <summary>Codex CLI's public OAuth client id (from codex-rs source).</summary>
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    private readonly HttpClient _http;

    public CodexHttpClient(HttpClient? http = null)
    {
        _http = http ?? EndpointUsageProvider.BuildHttpClient();
    }

    public async Task<CodexUsage> FetchUsageAsync(
        string accessToken, string? accountId, CancellationToken ct = default)
    {
        // wham/usage answers 429 (not 401) for a missing bearer; never send one.
        if (string.IsNullOrEmpty(accessToken))
            throw new ProviderUnauthorizedException();

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("TokenSpendie/1.0");
        if (accountId is { } id)
            request.Headers.Add("ChatGPT-Account-Id", id);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderNetworkException(ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (int)response.StatusCode switch
            {
                200 => Decode(body),
                // 403 is treated like 401 (refresh + retry): wham/usage answers
                // 403 for some expired-auth states — deliberate deviation from
                // the Claude endpoint, which only refreshes on 401.
                401 or 403 => throw new ProviderUnauthorizedException(),
                429 => throw new ProviderRateLimitedException(response.Headers.RetryAfter?.Delta),
                _ => throw new ProviderBadResponseException($"status {(int)response.StatusCode}"),
            };
        }
    }

    public async Task<CodexCredentials> RefreshAsync(CodexCredentials creds, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUrl);
        var payload = new JsonObject
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = creds.RefreshToken,
            ["scope"] = "openid profile email",
        };
        request.Content = new StringContent(
            payload.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderNetworkException(ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ProviderReauthRequiredException("Codex");

            JsonNode? json;
            try { json = JsonNode.Parse(body); }
            catch (Exception) { throw new ProviderReauthRequiredException("Codex"); }
            if (json is null)
                throw new ProviderReauthRequiredException("Codex");

            // Fields absent from the response keep their stored values.
            return creds with
            {
                AccessToken = json["access_token"]?.GetValue<string>() ?? creds.AccessToken,
                RefreshToken = json["refresh_token"]?.GetValue<string>() ?? creds.RefreshToken,
                IdToken = json["id_token"]?.GetValue<string>() ?? creds.IdToken,
            };
        }
    }

    /// <summary>Decodes the <c>wham/usage</c> body into raw windows. A window
    /// missing <c>used_percent</c> is treated as absent.</summary>
    public static CodexUsage Decode(string body)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(body); }
        catch (Exception) { throw new ProviderBadResponseException("malformed wham/usage payload"); }

        var rateLimit = root?["rate_limit"];

        static CodexWindow? Window(JsonNode? raw)
        {
            if (raw?["used_percent"]?.GetValue<double>() is not { } used) return null;
            DateTimeOffset? resetsAt = raw["reset_at"]?.GetValue<long>() is { } epoch
                ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                : null;
            var seconds = raw["limit_window_seconds"]?.GetValue<int>();
            return new CodexWindow(used, resetsAt, seconds);
        }

        return new CodexUsage(
            root?["plan_type"]?.GetValue<string>(),
            Window(rateLimit?["primary_window"]),
            Window(rateLimit?["secondary_window"]));
    }
}
