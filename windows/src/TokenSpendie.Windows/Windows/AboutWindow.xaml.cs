using System.Reflection;
using System.Windows;

namespace TokenSpendie.Windows.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";
        VersionText.Text = $"Version {version}";
    }
}
