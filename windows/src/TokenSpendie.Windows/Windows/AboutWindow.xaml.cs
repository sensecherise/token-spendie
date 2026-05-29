using System.Reflection;
using System.Windows;

namespace TokenSpendie.Windows.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {ReadInformationalVersion()}";
    }

    private static string ReadInformationalVersion()
    {
        var attr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr is null || string.IsNullOrEmpty(attr.InformationalVersion)) return "—";
        // Strip the "+<commit-sha>" build-metadata suffix SourceLink appends.
        var version = attr.InformationalVersion;
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
