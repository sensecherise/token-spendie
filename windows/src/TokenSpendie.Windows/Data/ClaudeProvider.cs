using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public sealed class ClaudeProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Claude;
    public string DisplayName => "Claude";

    private readonly ICredentialReader _credentials;
    private readonly IClaudeUsageEndpoint _endpoint;

    public ClaudeProvider(ICredentialReader credentials, IClaudeUsageEndpoint endpoint)
    {
        _credentials = credentials;
        _endpoint = endpoint;
    }

    public bool DetectCredentials() => _credentials.CredentialsExist();

    public async Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var creds = await _credentials.LoadCredentialsAsync(ct).ConfigureAwait(false);
        UsageSnapshot usage;
        try
        {
            usage = await _endpoint.FetchUsageAsync(creds.AccessToken, ct).ConfigureAwait(false);
        }
        catch (ProviderUnauthorizedException)
        {
            // Re-read credentials once — Claude Code refreshes the token during normal use.
            var refreshed = await _credentials.LoadCredentialsAsync(ct).ConfigureAwait(false);
            usage = await _endpoint.FetchUsageAsync(refreshed.AccessToken, ct).ConfigureAwait(false);
        }
        return Convert(usage);
    }

    /// <summary>Pure <see cref="UsageSnapshot"/> → <see cref="ProviderSnapshot"/> mapping.
    /// Session is the headline; <c>Windows</c> is <c>[session, weekly, model-weeklies…]</c>.</summary>
    public static ProviderSnapshot Convert(UsageSnapshot usage)
    {
        var session = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, usage.Session);
        var windows = new List<LabeledWindow> { session };
        windows.Add(new LabeledWindow("Weekly", "all models", ResetStyle.Date, usage.Weekly));
        foreach (var model in usage.ModelWeeklies)
        {
            windows.Add(new LabeledWindow(
                $"Weekly · {model.Model}", $"{model.Model} only",
                ResetStyle.Date, model.Window));
        }
        return new ProviderSnapshot(
            Id: ProviderID.Claude, Plan: null,
            Headline: session, Windows: windows,
            FetchedAt: usage.FetchedAt);
    }
}
