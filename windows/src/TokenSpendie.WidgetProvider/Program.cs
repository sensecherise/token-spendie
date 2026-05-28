using TokenSpendie.WidgetProvider.Com;

namespace TokenSpendie.WidgetProvider;

public static class Program
{
    [MTAThread]
    public static int Main(string[] args)
    {
        // The widget host launches us with "-RegisterProcessAsComServer" when it
        // wants to instantiate the provider. Without that switch we no-op.
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
            return 0;

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            using var manager = RegistrationManager<WidgetProvider>.RegisterProvider();
            using var disposedEvent = manager.GetDisposedEvent();
            disposedEvent.WaitOne();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
