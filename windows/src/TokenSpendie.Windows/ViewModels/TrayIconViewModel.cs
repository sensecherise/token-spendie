using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Tray;

namespace TokenSpendie.Windows.ViewModels;

public partial class TrayIconViewModel : ObservableObject
{
    private readonly UsageStore _store;
    private readonly PreferencesStore? _preferences;
    private double _dpiScale = 1.0;

    [ObservableProperty] private ImageSource? _iconSource;
    [ObservableProperty] private string _toolTipText = "Token Spendie — loading…";

    public event System.EventHandler? ShowPopupRequested;

    public TrayIconViewModel(UsageStore store, PreferencesStore? preferences = null)
    {
        _store = store;
        _preferences = preferences;
        _store.PropertyChanged += OnStorePropertyChanged;
        if (_preferences is not null) _preferences.PropertyChanged += OnPreferencesChanged;
        RecomputeFromStore();
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreferencesStore.Theme)) RecomputeFromStore();
    }

    /// <summary>Called by the host when the icon's monitor DPI changes.</summary>
    public void OnDpiChanged(double newScale)
    {
        _dpiScale = newScale;
        RecomputeFromStore();
    }

    [RelayCommand]
    private void LeftClick() => ShowPopupRequested?.Invoke(this, System.EventArgs.Empty);

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UsageStore.Providers))
            RecomputeFromStore();
    }

    private void RecomputeFromStore()
    {
        var headline = HeadlineProvider();
        var theme = _preferences?.Theme ?? Theme.Default;
        if (headline?.Snapshot is null)
        {
            // No data yet — show a faint placeholder ring so the tray icon
            // appears at app launch (H.NotifyIcon won't draw a null IconSource).
            IconSource = RingIconRenderer.Render(percent: 0, level: UsageLevel.Calm,
                dpiScale: _dpiScale, theme: theme);
            ToolTipText = "Token Spendie — loading…";
            return;
        }

        var percent = headline.Snapshot.Headline.Window.Percent;
        var level = UsageLevelExtensions.ForPercent(percent);
        IconSource = RingIconRenderer.Render(percent, level, _dpiScale, theme);
        ToolTipText = $"Token Spendie — {headline.DisplayName} {percent:F0}%";
    }

    private ProviderUsage? HeadlineProvider()
    {
        foreach (var u in _store.Providers)
        {
            if (u.State == LoadState.Ok || u.State == LoadState.Stale) return u;
        }
        return _store.Providers.Count > 0 ? _store.Providers[0] : null;
    }
}
