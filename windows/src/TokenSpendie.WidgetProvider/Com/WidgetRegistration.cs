using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace TokenSpendie.WidgetProvider.Com;

// COM hosting glue adapted from the official WindowsAppSDK Widgets sample:
// https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/Widgets/cs-console-packaged/WidgetHelper

internal static class Guids
{
    public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string IUnknown = "00000000-0000-0000-C000-000000000046";
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(Guids.IClassFactory)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig]
    int LockServer(bool fLock);
}

internal static class ClassObject
{
    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    public static void Register(Guid clsid, object pUnk, out uint cookie)
    {
        const uint CLSCTX_LOCAL_SERVER = 0x4;
        const uint REGCLS_MULTIPLEUSE = 0x1;
        int result = CoRegisterClassObject(clsid, pUnk, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out cookie);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
    }

    public static int Revoke(uint cookie) => CoRevokeClassObject(cookie);
}

internal sealed class WidgetProviderFactory<T> : IClassFactory
    where T : IWidgetProvider, new()
{
    private const int CLASS_E_NOAGGREGATION = -2147221232;
    private const int E_NOINTERFACE = -2147467262;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero)
            Marshal.ThrowExceptionForHR(CLASS_E_NOAGGREGATION);

        if (riid == typeof(T).GUID || riid == Guid.Parse(Guids.IUnknown))
            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
        else
            Marshal.ThrowExceptionForHR(E_NOINTERFACE);

        return 0;
    }

    public int LockServer(bool fLock) => 0;
}

public sealed class RegistrationManager<TWidgetProvider> : IDisposable
    where TWidgetProvider : IWidgetProvider, new()
{
    private readonly ManualResetEvent _disposedEvent = new(false);
    private readonly IDisposable _registeredProvider;
    private bool _disposed;

    private RegistrationManager(IDisposable provider)
    {
        _registeredProvider = provider;
    }

    public static RegistrationManager<TWidgetProvider> RegisterProvider()
    {
        var registration = RegisterClass(typeof(TWidgetProvider).GUID, new WidgetProviderFactory<TWidgetProvider>());
        return new RegistrationManager<TWidgetProvider>(registration);
    }

    private static IDisposable RegisterClass(Guid clsid, IClassFactory factory)
    {
        ClassObject.Register(clsid, factory, out uint handle);
        return new Unregisterer(handle);
    }

    public ManualResetEvent GetDisposedEvent() => _disposedEvent;

    public void Dispose()
    {
        if (_disposed) return;
        _registeredProvider.Dispose();
        _disposed = true;
        _disposedEvent.Set();
        GC.SuppressFinalize(this);
    }

    ~RegistrationManager() { Dispose(); }

    private sealed class Unregisterer : IDisposable
    {
        private readonly uint _handle;
        public Unregisterer(uint handle) { _handle = handle; }
        public void Dispose() { ClassObject.Revoke(_handle); }
    }
}
