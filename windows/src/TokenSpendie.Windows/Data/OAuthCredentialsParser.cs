using System.Text.Json;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public static class OAuthCredentialsParser
{
    public static OAuthCredentials Parse(ReadOnlySpan<byte> utf8Json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (JsonException ex)
        {
            throw new CredentialMalformedException("not JSON", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object)
            {
                throw new CredentialMalformedException("claudeAiOauth field missing");
            }

            if (!oauth.TryGetProperty("accessToken", out var atProp)
                || atProp.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(atProp.GetString()))
            {
                throw new CredentialMalformedException("accessToken missing or empty");
            }

            var accessToken = atProp.GetString()!;
            var refreshToken = oauth.TryGetProperty("refreshToken", out var rt)
                && rt.ValueKind == JsonValueKind.String
                    ? rt.GetString()
                    : null;

            DateTimeOffset? expiresAt = null;
            if (oauth.TryGetProperty("expiresAt", out var exp)
                && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetDouble(out var raw))
            {
                // Heuristic: values past year ~2001 in ms are > 1e12; treat those as milliseconds.
                var seconds = raw > 1_000_000_000_000 ? raw / 1000.0 : raw;
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
            }

            return new OAuthCredentials(accessToken, refreshToken, expiresAt);
        }
    }
}
