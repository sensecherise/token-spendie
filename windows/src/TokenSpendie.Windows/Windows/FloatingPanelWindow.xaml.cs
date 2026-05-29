using System.Windows;
using System.Windows.Input;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows.Windows;

public partial class FloatingPanelWindow : Window
{
    private FloatingPanelViewModel? _vm;

    public FloatingPanelWindow()
    {
        InitializeComponent();
    }

    public void Bind(FloatingPanelViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        Width = vm.Width;
        // Height is content-driven via SizeToContent="Height"; vm.Height is
        // ignored. We still persist Height on close for forward-compat, but
        // it won't be reapplied as long as SizeToContent stays set.
        if (vm.Left is { } l && vm.Top is { } t)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            var workArea = SystemParameters.WorkArea;
            Left = System.Math.Clamp(l, workArea.Left - Width + 60, workArea.Right - 60);
            Top = System.Math.Clamp(t, workArea.Top, workArea.Bottom - 60);
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is FrameworkElement el)
        {
            if (el is System.Windows.Controls.Button) return;
            try { DragMove(); } catch { /* DragMove throws if left button isn't actually down */ }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_vm is not null && WindowState == WindowState.Normal)
        {
            _vm.Save(Left, Top, Width, Height);
        }
        base.OnClosing(e);
    }
}
