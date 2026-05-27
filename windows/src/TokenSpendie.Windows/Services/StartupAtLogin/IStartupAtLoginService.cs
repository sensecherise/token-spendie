namespace TokenSpendie.Windows.Services.StartupAtLogin;

public interface IStartupAtLoginService
{
    bool IsEnabled();
    void Enable();
    void Disable();
}
