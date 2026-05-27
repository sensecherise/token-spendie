using Microsoft.Win32;

namespace TokenSpendie.Windows.Services;

public sealed class PowerEventObserver : IPowerEventObserver
{
    public event System.EventHandler? Resumed;

    public PowerEventObserver()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Resumed?.Invoke(this, System.EventArgs.Empty);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
