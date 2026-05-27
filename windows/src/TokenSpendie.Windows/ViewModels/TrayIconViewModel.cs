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
    private double _dpiScale = 1.0;

    [ObservableProperty] private ImageSource? _iconSource;
    [ObservableProperty] private string _toolTipText = "Token Spendie — loading…";

    public event System.EventHandler? ShowPopupRequested;

    public TrayIconViewModel(UsageStore store)
    {
        _store = store;
        _store.PropertyChanged += OnStorePropertyChanged;
        RecomputeFromStore();
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
        if (headline?.Snapshot is null)
        {
            ToolTipText = "Token Spendie — loading…";
            return;
        }

        var percent = headline.Snapshot.Headline.Window.Percent;
        var level = UsageLevelExtensions.ForPercent(percent);
        IconSource = RingIconRenderer.Render(percent, level, _dpiScale);
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
