using System;
using System.Windows;
using System.Windows.Input;

namespace TokenSpendie.Windows.Windows;

public partial class WidgetsBoardWindow : Window
{
    public WidgetsBoardWindow()
    {
        InitializeComponent();
        DateText.Text = DateTime.Now.ToString("MMM d");
        Activated += (_, _) => DateText.Text = DateTime.Now.ToString("MMM d");
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
