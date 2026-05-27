using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public sealed class GeminiProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Gemini;
    public string DisplayName => "Gemini";

    public const int DailyQuota = 1000;

    private readonly GeminiUsageReader _reader;

    public GeminiProvider(GeminiUsageReader? reader = null)
    {
        _reader = reader ?? new GeminiUsageReader();
    }

    public bool DetectCredentials() => _reader.DetectCredentials();

    public Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var count = _reader.RequestsToday();
        var snapshot = Convert(count, _reader.NextLocalMidnight(), _reader.Now());
        return Task.FromResult(snapshot);
    }

    public static ProviderSnapshot Convert(int count, DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var percent = count / (double)DailyQuota * 100;
        var window = new UsageWindow(percent, resetsAt);
        var daily = new LabeledWindow(
            Label: "Daily",
            Detail: $"≈{count} of {DailyQuota} requests",
            ResetStyle: ResetStyle.Countdown,
            Window: window);
        return new ProviderSnapshot(
            Id: ProviderID.Gemini, Plan: null,
            Headline: daily, Windows: new[] { daily },
            FetchedAt: now,
            Note: "estimate · counted from local logs");
    }
}
