using System.Runtime.InteropServices;
using System.Windows;

namespace TokenSpendie.Windows.Tray;

/// <summary>Native shell-rect + taskbar-edge probes for popup positioning.</summary>
public static class TrayIconLocator
{
    public enum TaskbarEdge { Left, Top, Right, Bottom }

    /// <summary>The screen rect (in physical pixels) of the tray icon
    /// identified by (hwnd, uID). Returns null if the icon is currently
    /// in the Win11 overflow flyout (Shell_NotifyIconGetRect returns E_FAIL).</summary>
    public static Rect? GetIconRect(nint hwnd, uint uID)
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = hwnd,
            uID = uID,
        };
        var hr = Shell_NotifyIconGetRect(ref id, out var rect);
        return hr == 0
            ? new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;
    }

    /// <summary>The taskbar edge (which screen side it docks to). Defaults to <see cref="TaskbarEdge.Bottom"/>
    /// if the call fails.</summary>
    public static TaskbarEdge GetTaskbarEdge()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        const uint ABM_GETTASKBARPOS = 5;
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == nint.Zero)
            return TaskbarEdge.Bottom;
        return data.uEdge switch
        {
            0 => TaskbarEdge.Left,
            1 => TaskbarEdge.Top,
            2 => TaskbarEdge.Right,
            3 => TaskbarEdge.Bottom,
            _ => TaskbarEdge.Bottom,
        };
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern nint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public System.Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }
}
