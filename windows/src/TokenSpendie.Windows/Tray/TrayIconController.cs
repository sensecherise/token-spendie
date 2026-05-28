using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using TokenSpendie.Windows.Windows;

namespace TokenSpendie.Windows.Tray;

public sealed class TrayIconController : System.IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly TrayIconViewModel _vm;
    private readonly DetailPanelViewModel _panelVm;
    private readonly PreferencesStore _preferences;
    private readonly PreferencesViewModel _prefsVm;
    private readonly FloatingPanelViewModel _floatingVm;
    private readonly IUpdateService _updates;
    private readonly IToastSender _toasts;

    private PopupWindow? _popup;
    private PreferencesWindow? _prefsWindow;
    private AboutWindow? _aboutWindow;
    private FloatingPanelWindow? _floatingWindow;
    private MenuItem? _lastCheckedItem;

    public TrayIconController(
        TrayIconViewModel vm,
        DetailPanelViewModel panelVm,
        PreferencesStore preferences,
        PreferencesViewModel prefsVm,
        FloatingPanelViewModel floatingVm,
        IUpdateService updates,
        IToastSender toasts)
    {
        _vm = vm;
        _panelVm = panelVm;
        _preferences = preferences;
        _prefsVm = prefsVm;
        _floatingVm = floatingVm;
        _updates = updates;
        _toasts = toasts;

        _icon = new TaskbarIcon
        {
            DataContext = _vm,
            ToolTipText = _vm.ToolTipText,
            Visibility = _preferences.ShowMenuBar ? Visibility.Visible : Visibility.Collapsed,
        };

        var iconBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.IconSource)) { Source = _vm };
        System.Windows.Data.BindingOperations.SetBinding(_icon, TaskbarIcon.IconSourceProperty, iconBinding);

        var toolTipBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.ToolTipText)) { Source = _vm };
        System.Windows.Data.BindingOperations.SetBinding(_icon, TaskbarIcon.ToolTipTextProperty, toolTipBinding);

        _icon.LeftClickCommand = _vm.LeftClickCommand;
        _icon.ContextMenu = BuildContextMenu();

        _vm.ShowPopupRequested += OnShowPopupRequested;
        _vm.OpenPreferencesRequested += (_, _) => OpenPreferences();
        _vm.OpenAboutRequested += (_, _) => OpenAbout();
        _vm.CheckForUpdatesRequested += async (_, _) => await OnCheckForUpdatesAsync().ConfigureAwait(false);

        _preferences.PropertyChanged += OnPrefsChanged;

        _icon.ForceCreate();

        ApplyFloatingPanelVisibility();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Refresh", Command = _vm.RefreshCommand });
        menu.Items.Add(new MenuItem { Header = "Preferences…", Command = _vm.OpenPreferencesCommand });
        menu.Items.Add(new MenuItem { Header = "About", Command = _vm.OpenAboutCommand });
        _lastCheckedItem = new MenuItem
        {
            Header = FormatLastChecked(_preferences.LastUpdateCheck),
            IsEnabled = false,
        };
        menu.Items.Add(_lastCheckedItem);
        menu.Items.Add(new MenuItem { Header = "Check for updates…", Command = _vm.CheckForUpdatesCommand });
        menu.Items.Add(new Separator());
        menu.Opened += (_, _) =>
        {
            if (_lastCheckedItem is not null)
                _lastCheckedItem.Header = FormatLastChecked(_preferences.LastUpdateCheck);
        };
        var launchItem = new MenuItem
        {
            Header = "Launch at login",
            IsCheckable = true,
            Command = _vm.ToggleLaunchAtLoginCommand,
        };
        var checkedBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.IsLaunchAtLogin))
        {
            Source = _vm,
            Mode = System.Windows.Data.BindingMode.OneWay,
        };
        System.Windows.Data.BindingOperations.SetBinding(launchItem, MenuItem.IsCheckedProperty, checkedBinding);
        menu.Items.Add(launchItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Quit", Command = _vm.QuitCommand });
        return menu;
    }

    private void OnShowPopupRequested(object? sender, System.EventArgs e)
    {
        if (_popup is { IsVisible: true }) { _popup.Hide(); return; }
        _popup ??= new PopupWindow { DataContext = _panelVm };
        PositionPopup(_popup);
        _popup.Show();
        _popup.Activate();
    }

    private void OpenPreferences()
    {
        if (_prefsWindow is null)
        {
            _prefsWindow = new PreferencesWindow { DataContext = _prefsVm };
            _prefsWindow.Closed += (_, _) => _prefsWindow = null;
        }
        _prefsWindow.Show();
        _prefsWindow.Activate();
    }

    private void OpenAbout()
    {
        if (_aboutWindow is null)
        {
            _aboutWindow = new AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        }
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private async System.Threading.Tasks.Task OnCheckForUpdatesAsync()
    {
        var result = await _updates.CheckAndApplyAsync().ConfigureAwait(false);
        _preferences.LastUpdateCheck = System.DateTimeOffset.UtcNow;

        var (title, body) = result switch
        {
            UpdateCheckResult.UpdateDownloaded => ("Update ready",
                "Token Spendie will switch to the new version next time it starts."),
            UpdateCheckResult.NoUpdate => ("Up to date",
                "You're on the latest version."),
            UpdateCheckResult.NotInstalled => ("Update not available",
                "This build wasn't installed via the official installer."),
            UpdateCheckResult.UpdateAvailable => ("Update available",
                "Couldn't download the update right now. Try again later."),
            _ => ("Update check failed",
                "Couldn't reach the update server."),
        };
        _toasts.SendInformational(title, body);
    }

    private void OnPrefsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreferencesStore.ShowMenuBar))
        {
            _icon.Visibility = _preferences.ShowMenuBar ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (e.PropertyName == nameof(PreferencesStore.ShowFloatingPanel))
        {
            ApplyFloatingPanelVisibility();
        }
    }

    private void ApplyFloatingPanelVisibility()
    {
        if (_preferences.ShowFloatingPanel)
        {
            _floatingWindow ??= new FloatingPanelWindow();
            _floatingWindow.Bind(_floatingVm);
            _floatingWindow.Show();
        }
        else
        {
            _floatingWindow?.Hide();
        }
    }

    internal static string FormatLastChecked(System.DateTimeOffset? when)
    {
        if (when is null) return "Last checked: never";
        var ago = System.DateTimeOffset.UtcNow - when.Value;
        if (ago < System.TimeSpan.Zero) return "Last checked: just now";
        if (ago < System.TimeSpan.FromMinutes(1)) return "Last checked: just now";
        if (ago < System.TimeSpan.FromHours(1)) return $"Last checked: {(int)ago.TotalMinutes}m ago";
        if (ago < System.TimeSpan.FromDays(1)) return $"Last checked: {(int)ago.TotalHours}h ago";
        return $"Last checked: {(int)ago.TotalDays}d ago";
    }

    private static void PositionPopup(PopupWindow popup)
    {
        GetCursorPos(out var pt);
        var work = SystemParameters.WorkArea;
        popup.Left = System.Math.Clamp(pt.X, work.Left, work.Right - 320);
        popup.Top = System.Math.Clamp(pt.Y, work.Top, work.Bottom - 200);
    }

    public void Dispose()
    {
        _icon.Dispose();
        _popup?.Close();
        _prefsWindow?.Close();
        _aboutWindow?.Close();
        _floatingWindow?.Close();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
