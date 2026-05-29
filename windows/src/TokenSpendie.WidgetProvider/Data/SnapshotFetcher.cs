using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.WidgetProvider.Data;

internal static class SnapshotFetcher
{
    public static UsageSnapshot GetCurrent()
    {
        var claude = new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider());
        if (claude.DetectCredentials())
        {
            try
            {
                var t = claude.FetchUsageAsync();
                t.Wait();
                return Convert(t.Result);
            }
            catch
            {
                // Fall through to Gemini.
            }
        }

        var gemini = new GeminiProvider();
        if (gemini.DetectCredentials())
        {
            var t = gemini.FetchUsageAsync();
            t.Wait();
            return Convert(t.Result);
        }

        return Empty();
    }

    private static UsageSnapshot Convert(ProviderSnapshot provider) =>
        new UsageSnapshot(
            Session: provider.Headline.Window,
            Weekly: provider.Windows.Count > 1 ? provider.Windows[1].Window : new UsageWindow(0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: provider.FetchedAt);

    private static UsageSnapshot Empty() =>
        new UsageSnapshot(
            Session: new UsageWindow(0, null),
            Weekly: new UsageWindow(0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: DateTimeOffset.UtcNow);
}
