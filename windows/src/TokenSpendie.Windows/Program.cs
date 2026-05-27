using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var verbose = args.Any(a => a == "--verbose");

        IUsageProvider[] providers =
        {
            new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
            new GeminiProvider(),
        };

        foreach (var provider in providers)
        {
            await PrintProviderAsync(provider, verbose).ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task PrintProviderAsync(IUsageProvider provider, bool verbose)
    {
        Console.WriteLine($"=== {provider.DisplayName} ===");

        if (!provider.DetectCredentials())
        {
            Console.WriteLine("  (not logged in)");
            Console.WriteLine();
            return;
        }

        try
        {
            var snapshot = await provider.FetchUsageAsync().ConfigureAwait(false);
            PrintSnapshot(snapshot);
        }
        catch (CredentialNotFoundException) { Console.WriteLine("  (credential file removed mid-run)"); }
        catch (CredentialMalformedException ex) { Console.WriteLine($"  malformed credentials: {ex.Message}"); }
        catch (ProviderUnauthorizedException) { Console.WriteLine("  401 — login expired"); }
        catch (ProviderRateLimitedException ex)
        {
            Console.WriteLine($"  429 — rate-limited (retry-after: {ex.RetryAfter?.TotalSeconds ?? -1}s)");
        }
        catch (ProviderNetworkException ex) { Console.WriteLine($"  network error: {ex.InnerException?.Message}"); }
        catch (ProviderBadResponseException ex) { Console.WriteLine($"  bad response: {ex.Message}"); }
        catch (Exception ex) when (verbose)
        {
            Console.WriteLine($"  unexpected: {ex}");
        }
        Console.WriteLine();
    }

    private static void PrintSnapshot(ProviderSnapshot snapshot)
    {
        if (snapshot.Plan is { } plan) Console.WriteLine($"  plan: {plan}");
        foreach (var w in snapshot.Windows)
        {
            var reset = w.Window.ResetsAt is { } r
                ? $" (resets {r.ToLocalTime():u})"
                : "";
            Console.WriteLine($"  {w.Label,-20} {w.Window.Percent,6:F1}%  {w.Detail}{reset}");
        }
        if (snapshot.Note is { } note) Console.WriteLine($"  note: {note}");
        Console.WriteLine($"  fetched at {snapshot.FetchedAt.ToLocalTime():u}");
    }
}
