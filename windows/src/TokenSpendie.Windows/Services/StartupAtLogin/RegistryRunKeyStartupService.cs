using Microsoft.Win32;

namespace TokenSpendie.Windows.Services.StartupAtLogin;

public sealed class RegistryRunKeyStartupService : IStartupAtLoginService
{
    private const string DefaultKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _subKey;
    private readonly string _valueName;
    private readonly string _exePath;

    public RegistryRunKeyStartupService() : this(DefaultKey, "TokenSpendie", CurrentExePath()) { }

    public RegistryRunKeyStartupService(string subKey, string valueName, string exePath)
    {
        _subKey = subKey;
        _valueName = valueName;
        _exePath = exePath;
    }

    private static string CurrentExePath() =>
        System.Environment.ProcessPath ?? System.AppContext.BaseDirectory;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        var raw = key?.GetValue(_valueName) as string;
        if (string.IsNullOrEmpty(raw)) return false;
        return raw.Contains($"\"{_exePath}\"", System.StringComparison.OrdinalIgnoreCase);
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey)
            ?? throw new System.InvalidOperationException("Cannot open HKCU Run subkey for write.");
        key.SetValue(_valueName, $"\"{_exePath}\" --hidden");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}
