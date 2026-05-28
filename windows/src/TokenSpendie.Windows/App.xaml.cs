using System.ComponentModel;
using System.Windows;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Services.StartupAtLogin;
using TokenSpendie.Windows.Tray;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows;

public partial class App : Application
{
    private PreferencesStore? _preferences;
    private UsageStore? _store;
    private UsageNotifier? _notifier;
    private TrayIconController? _tray;
    private INetworkAvailabilityObserver? _network;
    private IPowerEventObserver? _power;
    private IStartupAtLoginService? _startup;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        // MUST be first. Velopack uses this hook to handle install / uninstall /
        // first-run / update commands passed on the command line. If any UI is
        // touched before Run() the updater behaves incorrectly.
        Velopack.VelopackApp.Build().Run();

        AumidRegistrar.Register();

        _preferences = new PreferencesStore();
        _startup = new RegistryRunKeyStartupService();
        StartupReconciler.Reconcile(_preferences.LaunchAtLogin, _startup);
        _preferences.PropertyChanged += OnPreferencesChanged;

        _network = new NetworkAvailabilityObserver();
        _power = new PowerEventObserver();

        var toastSender = new WinRtToastSender();
        _notifier = new UsageNotifier(toastSender);
        var updateSvc = new VelopackUpdateService("https://github.com/sensecherise/token-spendie");

        var providers = new IUsageProvider[]
        {
            new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
            new GeminiProvider(),
        };
        _store = new UsageStore(
            providers,
            preferences: _preferences,
            network: _network,
            power: _power);

        _store.PropertyChanged += OnStorePropertyChanged;
        _store.Start();

        var trayVm = new TrayIconViewModel(_store, _preferences);
        var panelVm = new DetailPanelViewModel(_store);
        var prefsVm = new PreferencesViewModel(_preferences);
        var floatingVm = new FloatingPanelViewModel(_preferences, panelVm);
        _tray = new TrayIconController(trayVm, panelVm, _preferences, prefsVm, floatingVm, updateSvc, toastSender);
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsageStore.Providers))
        {
            _notifier?.Check(_store!.Providers);
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PreferencesStore.LaunchAtLogin)) return;
        if (_startup is null || _preferences is null) return;
        if (_preferences.LaunchAtLogin && !_startup.IsEnabled()) _startup.Enable();
        else if (!_preferences.LaunchAtLogin && _startup.IsEnabled()) _startup.Disable();
    }

    private async void App_Exit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        _network?.Dispose();
        _power?.Dispose();
        if (_store is not null) await _store.DisposeAsync();
    }
}
