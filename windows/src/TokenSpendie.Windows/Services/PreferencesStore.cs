using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

public sealed class PreferencesStore : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly string _path;

    private bool _showMenuBar = true;
    private bool _showFloatingPanel = false;
    private RefreshInterval _refreshInterval = RefreshInterval.S60;
    private bool _launchAtLogin = false;
    private Theme _theme = Theme.Default;
    private ProviderID _menuBarProviderID = ProviderID.Claude;

    public bool ShowMenuBar { get => _showMenuBar; set => SetAndPersist(ref _showMenuBar, value); }
    public bool ShowFloatingPanel { get => _showFloatingPanel; set => SetAndPersist(ref _showFloatingPanel, value); }
    public RefreshInterval RefreshInterval { get => _refreshInterval; set => SetAndPersist(ref _refreshInterval, value); }
    public bool LaunchAtLogin { get => _launchAtLogin; set => SetAndPersist(ref _launchAtLogin, value); }
    public Theme Theme { get => _theme; set => SetAndPersist(ref _theme, value); }
    public ProviderID MenuBarProviderID { get => _menuBarProviderID; set => SetAndPersist(ref _menuBarProviderID, value); }

    public PreferencesStore() : this(DefaultPath()) { }

    public PreferencesStore(string path)
    {
        _path = path;
        Load();
    }

    public static string DefaultPath() =>
        Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "TokenSpendie", "prefs.json");

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            using var stream = File.OpenRead(_path);
            var dto = JsonSerializer.Deserialize<PreferencesDto>(stream, JsonOptions);
            if (dto is null) return;
            _showMenuBar = dto.ShowMenuBar ?? _showMenuBar;
            _showFloatingPanel = dto.ShowFloatingPanel ?? _showFloatingPanel;
            _refreshInterval = dto.RefreshIntervalSeconds is { } secs
                ? RefreshIntervalExtensions.FromSeconds(secs)
                : _refreshInterval;
            _launchAtLogin = dto.LaunchAtLogin ?? _launchAtLogin;
            _theme = dto.Theme ?? _theme;
            _menuBarProviderID = dto.MenuBarProviderID ?? _menuBarProviderID;
        }
        catch { /* defaults survive */ }
    }

    private void Save()
    {
        try
        {
            var parent = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            var dto = new PreferencesDto
            {
                ShowMenuBar = _showMenuBar,
                ShowFloatingPanel = _showFloatingPanel,
                RefreshIntervalSeconds = _refreshInterval.Seconds(),
                LaunchAtLogin = _launchAtLogin,
                Theme = _theme,
                MenuBarProviderID = _menuBarProviderID,
            };
            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, dto, JsonOptions);
        }
        catch { /* best-effort */ }
    }

    private void SetAndPersist<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(storage, value)) return;
        storage = value;
        Save();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static readonly JsonSerializerOptions JsonOptions = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opts;
    }

    private sealed class PreferencesDto
    {
        public bool? ShowMenuBar { get; set; }
        public bool? ShowFloatingPanel { get; set; }
        public int? RefreshIntervalSeconds { get; set; }
        public bool? LaunchAtLogin { get; set; }
        public Theme? Theme { get; set; }
        public ProviderID? MenuBarProviderID { get; set; }
    }
}
