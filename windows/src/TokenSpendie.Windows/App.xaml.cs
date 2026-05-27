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
        AumidRegistrar.Register();

        _preferences = new PreferencesStore();
        _startup = new RegistryRunKeyStartupService();
        StartupReconciler.Reconcile(_preferences.LaunchAtLogin, _startup);
        _preferences.PropertyChanged += OnPreferencesChanged;

        _network = new NetworkAvailabilityObserver();
        _power = new PowerEventObserver();

        _notifier = new UsageNotifier(new WinRtToastSender());

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

        var trayVm = new TrayIconViewModel(_store);
        var panelVm = new DetailPanelViewModel(_store);
        _tray = new TrayIconController(trayVm, panelVm);
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
