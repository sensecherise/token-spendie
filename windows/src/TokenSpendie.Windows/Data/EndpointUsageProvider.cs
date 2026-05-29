using System.Net.Http.Headers;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public sealed class EndpointUsageProvider : IClaudeUsageEndpoint
{
    private static readonly Uri UsageUrl = new("https://api.anthropic.com/api/oauth/usage");

    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _now;

    public EndpointUsageProvider(HttpClient http, Func<DateTimeOffset>? now = null)
    {
        _http = http;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Default constructor — production wiring. <see cref="BuildHttpClient"/>
    /// applies the spec G8 connection pooling.</summary>
    public EndpointUsageProvider() : this(BuildHttpClient()) { }

    public static HttpClient BuildHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        return new HttpClient(handler);
    }

    public async Task<UsageSnapshot> FetchUsageAsync(string accessToken, CancellationToken ct = default)
    {
        // The endpoint returns 429 (not 401) for a missing/empty bearer, which would be
        // mislabeled as a rate limit. Never send an empty token.
        if (string.IsNullOrEmpty(accessToken))
            throw new ProviderUnauthorizedException();

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("TokenSpendie/1.0");

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
            var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return (int)response.StatusCode switch
            {
                200 => UsageDecoder.Decode(body, _now()),
                401 => throw new ProviderUnauthorizedException(),
                429 => throw new ProviderRateLimitedException(response.Headers.RetryAfter?.Delta),
                _ => throw new ProviderBadResponseException($"status {(int)response.StatusCode}"),
            };
        }
    }
}
