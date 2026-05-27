namespace TokenSpendie.Windows.Services;

/// <summary>One toast notification to send.</summary>
public record Joke(string Title, string Body);

/// <summary>Wraps the WinRT toast machinery so <see cref="UsageNotifier"/>
/// can be tested without invoking real toasts.</summary>
public interface IToastSender
{
    /// <summary>Sends the joke as a toast. <paramref name="dedupTag"/> is a stable
    /// per-window-threshold id; the same tag replaces any earlier toast.</summary>
    void Send(Joke joke, string dedupTag);
}
