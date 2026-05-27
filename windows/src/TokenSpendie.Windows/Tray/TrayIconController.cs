using System.Windows;
using H.NotifyIcon;
using TokenSpendie.Windows.ViewModels;
using TokenSpendie.Windows.Windows;

namespace TokenSpendie.Windows.Tray;

public sealed class TrayIconController : System.IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly TrayIconViewModel _vm;
    private readonly DetailPanelViewModel _panelVm;
    private PopupWindow? _popup;

    public TrayIconController(TrayIconViewModel vm, DetailPanelViewModel panelVm)
    {
        _vm = vm;
        _panelVm = panelVm;
        _icon = new TaskbarIcon
        {
            DataContext = _vm,
            ToolTipText = _vm.ToolTipText,
            Visibility = Visibility.Visible,
        };
        var iconBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.IconSource))
        {
            Source = _vm,
        };
        System.Windows.Data.BindingOperations.SetBinding(_icon,
            TaskbarIcon.IconSourceProperty, iconBinding);
        var toolTipBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.ToolTipText))
        {
            Source = _vm,
        };
        System.Windows.Data.BindingOperations.SetBinding(_icon,
            TaskbarIcon.ToolTipTextProperty, toolTipBinding);
        _icon.LeftClickCommand = _vm.LeftClickCommand;
        _vm.ShowPopupRequested += OnShowPopupRequested;
    }

    private void OnShowPopupRequested(object? sender, System.EventArgs e)
    {
        if (_popup is { IsVisible: true })
        {
            _popup.Hide();
            return;
        }
        _popup ??= new PopupWindow { DataContext = _panelVm };
        PositionPopup(_popup);
        _popup.Show();
        _popup.Activate();
    }

    private void PositionPopup(PopupWindow popup)
    {
        GetCursorPos(out var pt);
        var work = SystemParameters.WorkArea;
        popup.Left = System.Math.Clamp(pt.X, work.Left, work.Right - 320);
        popup.Top = System.Math.Clamp(pt.Y, work.Top, work.Bottom - 200);
    }

    public void Dispose()
    {
        _icon.Dispose();
        _popup?.Close();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
