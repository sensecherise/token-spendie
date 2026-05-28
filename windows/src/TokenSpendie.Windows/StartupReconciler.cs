using TokenSpendie.Windows.Services.StartupAtLogin;

namespace TokenSpendie.Windows;

/// <summary>Reconciles the registry "launch at login" entry with the user's
/// persisted preference. Always treats prefs as source of truth.</summary>
internal static class StartupReconciler
{
    public static void Reconcile(bool preferenceWantsEnabled, IStartupAtLoginService startup)
    {
        var registryEnabled = startup.IsEnabled();
        if (preferenceWantsEnabled && !registryEnabled) startup.Enable();
        else if (!preferenceWantsEnabled && registryEnabled) startup.Disable();
    }
}
