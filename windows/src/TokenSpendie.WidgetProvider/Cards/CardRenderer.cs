using TokenSpendie.Windows.Models;

namespace TokenSpendie.WidgetProvider.Cards;

/// <summary>Stub renderer; Task 5/6 replaces with real Adaptive Card 1.5 output.</summary>
public sealed class CardRenderer
{
    public string Render(string kind, string size, UsageSnapshot snapshot)
        => "{ \"type\": \"AdaptiveCard\", \"version\": \"1.5\", \"body\": [] }";

    public static string RenderError(string message)
        => "{ \"type\": \"AdaptiveCard\", \"version\": \"1.5\", \"body\": [] }";
}
