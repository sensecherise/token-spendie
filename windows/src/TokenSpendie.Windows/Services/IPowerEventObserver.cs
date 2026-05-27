namespace TokenSpendie.Windows.Services;

public interface IPowerEventObserver : System.IDisposable
{
    /// <summary>Raised when the host resumes from sleep/hibernate.</summary>
    event System.EventHandler? Resumed;
}
