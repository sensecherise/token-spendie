using System.Reflection;
using AdaptiveCards.Templating;
using TokenSpendie.WidgetProvider.Rendering;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.WidgetProvider.Cards;

public sealed class CardRenderer
{
    private readonly Lazy<string> _sessionTemplate = new(() => LoadTemplate("SessionCard.json"));
    private readonly Lazy<string> _fullTemplate = new(() => LoadTemplate("FullCard.json"));

    public string Render(string kind, string size, UsageSnapshot snapshot) => kind switch
    {
        "TokenSpendie.Session" => RenderSession(snapshot),
        "TokenSpendie.Full" => RenderFull(snapshot, size),
        _ => RenderError($"Unknown widget kind: {kind}"),
    };

    public string RenderSession(UsageSnapshot snapshot)
    {
        var pct = snapshot.Session.Percent;
        var level = UsageLevelExtensions.ForPercent(pct);
        var ringPng = RingPngRenderer.RenderBase64(pct, level);

        var data = new
        {
            ringDataUri = $"data:image/png;base64,{ringPng}",
            percentText = $"{pct:F0}%",
            footerText = FormatFooter(snapshot),
        };

        var template = new AdaptiveCardTemplate(_sessionTemplate.Value);
        return template.Expand(data);
    }

    public string RenderFull(UsageSnapshot snapshot, string size)
    {
        // Task 6 replaces this with a real medium-size card. For now, fall
        // back to the session card so widget hosts still get valid JSON.
        return RenderSession(snapshot);
    }

    public static string RenderError(string message)
    {
        var card = new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                new { type = "TextBlock", text = "Token Spendie", weight = "Bolder" },
                new { type = "TextBlock", text = message, wrap = true, isSubtle = true },
            },
        };
        return System.Text.Json.JsonSerializer.Serialize(card);
    }

    private static string FormatFooter(UsageSnapshot snapshot)
    {
        var ago = DateTimeOffset.UtcNow - snapshot.FetchedAt;
        if (ago < TimeSpan.FromMinutes(1)) return "Just refreshed";
        if (ago < TimeSpan.FromHours(1)) return $"Refreshed {(int)ago.TotalMinutes}m ago";
        if (ago < TimeSpan.FromDays(1)) return $"Refreshed {(int)ago.TotalHours}h ago";
        return $"Refreshed {(int)ago.TotalDays}d ago";
    }

    private static string LoadTemplate(string name)
    {
        // Card templates are embedded resources — see the csproj. Default MSBuild
        // resource ID is "<RootNamespace>.<Directory>.<File>".
        var asm = Assembly.GetExecutingAssembly();
        var resource = $"TokenSpendie.WidgetProvider.Cards.{name}";
        using var stream = asm.GetManifestResourceStream(resource)
                          ?? throw new FileNotFoundException(
                              $"Embedded card template '{resource}' not found in {asm.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
