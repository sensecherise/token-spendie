using System.Windows;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Tray;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows;

public partial class App : Application
{
    private UsageStore? _store;
    private TrayIconController? _tray;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        var providers = new IUsageProvider[]
        {
            new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
            new GeminiProvider(),
        };
        _store = new UsageStore(providers);
        _store.Start();

        var trayVm = new TrayIconViewModel(_store);
        var panelVm = new DetailPanelViewModel(_store);
        _tray = new TrayIconController(trayVm, panelVm);
    }

    private async void App_Exit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_store is not null) await _store.DisposeAsync();
    }
}
