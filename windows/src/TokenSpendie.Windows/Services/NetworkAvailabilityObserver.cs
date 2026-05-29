using System.Net.NetworkInformation;

namespace TokenSpendie.Windows.Services;

public sealed class NetworkAvailabilityObserver : INetworkAvailabilityObserver
{
    public event System.EventHandler? Reconnected;

    private bool _wasAvailable = NetworkInterface.GetIsNetworkAvailable();

    public NetworkAvailabilityObserver()
    {
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var now = e.IsAvailable;
        var reconnected = now && !_wasAvailable;
        _wasAvailable = now;
        if (reconnected)
            Reconnected?.Invoke(this, System.EventArgs.Empty);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
    }
}
