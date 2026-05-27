using System.Windows;
using System.Windows.Input;

namespace TokenSpendie.Windows.Windows;

public partial class PopupWindow : Window
{
    public PopupWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }
}
