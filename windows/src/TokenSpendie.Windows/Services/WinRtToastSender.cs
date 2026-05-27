using Microsoft.Toolkit.Uwp.Notifications;

namespace TokenSpendie.Windows.Services;

public sealed class WinRtToastSender : IToastSender
{
    public void Send(Joke joke, string dedupTag)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(joke.Title)
                .AddText(joke.Body)
                .AddArgument("threshold", dedupTag)
                .Show(toast =>
                {
                    toast.Tag = dedupTag;
                    toast.Group = "tokenspendie";
                });
        }
        catch
        {
            // AUMID not registered, or toast subsystem unavailable — fail silently.
        }
    }
}
