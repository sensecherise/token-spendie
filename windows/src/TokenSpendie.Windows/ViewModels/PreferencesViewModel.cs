using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.ViewModels;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly PreferencesStore _prefs;

    public IReadOnlyList<RefreshInterval> Intervals { get; } =
        new[] { RefreshInterval.S60, RefreshInterval.S120 };

    public IReadOnlyList<Theme> Themes { get; } =
        new[] { Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet };

    public PreferencesViewModel(PreferencesStore prefs)
    {
        _prefs = prefs;
        _prefs.PropertyChanged += OnPrefsChanged;
    }

    public bool ShowMenuBar
    {
        get => _prefs.ShowMenuBar;
        set
        {
            if (_prefs.ShowMenuBar == value) return;
            _prefs.ShowMenuBar = value;
            EnforceAtLeastOneSurface(Surface.MenuBar);
            OnPropertyChanged();
        }
    }

    public bool ShowFloatingPanel
    {
        get => _prefs.ShowFloatingPanel;
        set
        {
            if (_prefs.ShowFloatingPanel == value) return;
            _prefs.ShowFloatingPanel = value;
            EnforceAtLeastOneSurface(Surface.Floating);
            OnPropertyChanged();
        }
    }

    public RefreshInterval RefreshInterval
    {
        get => _prefs.RefreshInterval;
        set { if (_prefs.RefreshInterval != value) { _prefs.RefreshInterval = value; OnPropertyChanged(); } }
    }

    public Theme Theme
    {
        get => _prefs.Theme;
        set { if (_prefs.Theme != value) { _prefs.Theme = value; OnPropertyChanged(); } }
    }

    public bool LaunchAtLogin
    {
        get => _prefs.LaunchAtLogin;
        set { if (_prefs.LaunchAtLogin != value) { _prefs.LaunchAtLogin = value; OnPropertyChanged(); } }
    }

    [RelayCommand]
    private void Quit() => System.Windows.Application.Current?.Shutdown();

    private void OnPrefsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PreferencesStore.ShowMenuBar): OnPropertyChanged(nameof(ShowMenuBar)); break;
            case nameof(PreferencesStore.ShowFloatingPanel): OnPropertyChanged(nameof(ShowFloatingPanel)); break;
            case nameof(PreferencesStore.RefreshInterval): OnPropertyChanged(nameof(RefreshInterval)); break;
            case nameof(PreferencesStore.Theme): OnPropertyChanged(nameof(Theme)); break;
            case nameof(PreferencesStore.LaunchAtLogin): OnPropertyChanged(nameof(LaunchAtLogin)); break;
        }
    }

    private void EnforceAtLeastOneSurface(Surface changed)
    {
        if (_prefs.ShowMenuBar || _prefs.ShowFloatingPanel) return;
        switch (changed)
        {
            case Surface.MenuBar: _prefs.ShowFloatingPanel = true; break;
            case Surface.Floating: _prefs.ShowMenuBar = true; break;
        }
    }
}
