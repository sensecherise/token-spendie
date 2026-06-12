using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.Data;

/// <summary>
/// The <see cref="IUsageProvider"/> for Google Antigravity. Best-effort: reads
/// per-model quota from the language server the Antigravity IDE / <c>agy</c> CLI
/// runs locally (see <see cref="AntigravityProbe"/>). No credentials are read or
/// stored.
///
/// Row semantics: the row exists while the process is running OR a cached
/// snapshot is newer than 7 days, so quitting Antigravity leaves a stale row
/// instead of dropping it.
/// </summary>
public sealed class AntigravityProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Antigravity;
    public string DisplayName => "Antigravity";

    /// <summary>How long a cached snapshot keeps the row alive without a
    /// process.</summary>
    public static readonly TimeSpan CacheRowTtl = TimeSpan.FromDays(7);

    private readonly IAntigravityProbe _probe;
    private readonly SnapshotCache _cache;
    private readonly Func<DateTimeOffset> _now;

    public AntigravityProvider(IAntigravityProbe probe, SnapshotCache cache, Func<DateTimeOffset>? now = null)
    {
        _probe = probe;
        _cache = cache;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Production wiring. The cache is a read-only handle on the same
    /// file <see cref="UsageStore"/> writes for this provider — used solely for
    /// the row-TTL check above.</summary>
    public AntigravityProvider()
    {
        _probe = new AntigravityProbe();
        _cache = new SnapshotCache(SnapshotCache.DefaultPathFor(ProviderID.Antigravity));
        _now = () => DateTimeOffset.UtcNow;
    }

    public bool DetectCredentials()
    {
        if (_probe.IsProcessPresent()) return true;
        var cached = _cache.Load();
        return cached is not null && (_now() - cached.FetchedAt) < CacheRowTtl;
    }

    public async Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var quota = await _probe.FetchQuotaDataAsync(ct).ConfigureAwait(false);
        return Decode(quota, _now());
    }

    // MARK: - Decoding

    /// <summary>Maps a quota payload to one window per model config that reports
    /// a <c>remainingFraction</c>. Headline = the highest-used window.</summary>
    public static ProviderSnapshot Decode(AntigravityQuotaData quota, DateTimeOffset fetchedAt)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(quota.Body); }
        catch (Exception) { throw new ProviderBadResponseException("malformed Antigravity payload"); }
        if (root is null)
            throw new ProviderBadResponseException("malformed Antigravity payload");

        // A non-zero / non-"ok" top-level `code` is an RPC error envelope.
        if (root["code"] is { } code)
        {
            var text = code.ToString().ToLowerInvariant();
            if (text is not ("0" or "ok" or "success"))
                throw new ProviderBadResponseException($"RPC error code {text}");
        }

        IReadOnlyList<JsonNode?> configs;
        string? plan;
        switch (quota.Source)
        {
            case AntigravityQuotaSource.UserStatus:
                if (root["userStatus"] is not JsonObject userStatus)
                    throw new ProviderBadResponseException("missing userStatus");
                configs = (userStatus["cascadeModelConfigData"]?["clientModelConfigs"] as JsonArray)
                    ?.ToList() ?? new List<JsonNode?>();
                plan = PlanName(userStatus);
                break;
            default: // CommandModelConfigs
                configs = (root["clientModelConfigs"] as JsonArray)?.ToList() ?? new List<JsonNode?>();
                plan = null;
                break;
        }

        // JSON doesn't distinguish int from float on the wire; read numbers
        // tolerantly so "0.82" or a non-numeric value degrades to the field
        // being absent instead of discarding the whole payload.
        static double? Number(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

        var windows = new List<LabeledWindow>();
        foreach (var config in configs)
        {
            if (config?["label"]?.GetValue<string>() is not { } label) continue;
            if (config["quotaInfo"] is not JsonObject quotaInfo) continue;
            if (Number(quotaInfo["remainingFraction"]) is not { } remaining) continue;

            // Lower-bounded only — UsageWindow.Percent may exceed 100 by
            // convention (over-cap), matching the other providers.
            var percent = Math.Max((1 - remaining) * 100, 0);
            var resetsAt = quotaInfo["resetTime"]?.GetValue<string>() is { } reset
                ? ParseResetTime(reset)
                : null;
            windows.Add(new LabeledWindow(
                label, "model quota", ResetStyle.Countdown, new UsageWindow(percent, resetsAt)));
        }
        if (windows.Count == 0)
            throw new ProviderBadResponseException("no model quotas in Antigravity payload");

        var headline = windows.MaxBy(w => w.Window.Percent)!;
        return new ProviderSnapshot(
            Id: ProviderID.Antigravity, Plan: plan,
            Headline: headline, Windows: windows,
            FetchedAt: fetchedAt, Note: "live while Antigravity runs");
    }

    /// <summary><c>userTier.name</c> is the real subscription tier;
    /// <c>planInfo</c> names are the fallback chain (CodexBar-verified preference
    /// order).</summary>
    private static string? PlanName(JsonObject userStatus)
    {
        if ((userStatus["userTier"]?["name"]?.GetValue<string>())?.Trim() is { Length: > 0 } tier)
            return tier;
        if (userStatus["planStatus"]?["planInfo"] is not JsonObject planInfo) return null;
        foreach (var key in new[] { "planDisplayName", "displayName", "productName", "planName", "planShortName" })
        {
            if ((planInfo[key]?.GetValue<string>())?.Trim() is { Length: > 0 } value)
                return value;
        }
        return null;
    }

    /// <summary><c>resetTime</c> arrives as ISO-8601 or epoch-seconds-as-string.</summary>
    private static DateTimeOffset? ParseResetTime(string value)
    {
        if (DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
        return null;
    }
}
