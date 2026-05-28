using System.Windows;
using System.Windows.Controls;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows.Windows;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm
            && sender is Button { Tag: Theme theme })
        {
            vm.Theme = theme;
        }
    }
}
