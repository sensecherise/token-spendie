namespace TokenSpendie.Windows.Services;

/// <summary>Registers the AUMID + COM server required to surface toasts from an
/// unpackaged WPF app. Safe to call multiple times — Windows ignores duplicates.</summary>
public static class AumidRegistrar
{
    public const string Aumid = "Sensecherise.TokenSpendie";

    public static void Register()
    {
        try
        {
            Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.OnActivated += _ => { };
        }
        catch
        {
            // Silently swallow — toasts will simply fail to surface, the rest of the app continues.
        }
    }
}
