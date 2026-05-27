using System.Windows;

namespace TokenSpendie.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Services + tray are wired in Task 14.
    }
}
