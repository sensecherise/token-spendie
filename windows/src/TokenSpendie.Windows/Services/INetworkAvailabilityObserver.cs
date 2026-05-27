namespace TokenSpendie.Windows.Services;

public interface INetworkAvailabilityObserver : System.IDisposable
{
    /// <summary>Raised when the host transitions from offline → online.</summary>
    event System.EventHandler? Reconnected;
}
