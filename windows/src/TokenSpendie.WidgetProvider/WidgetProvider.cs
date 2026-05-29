using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using TokenSpendie.WidgetProvider.Cards;
using TokenSpendie.WidgetProvider.Data;

namespace TokenSpendie.WidgetProvider;

// The GUID below MUST match the COM activation entry in Package.appxmanifest
// (Task 7). Locked in Task 2.
[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("579614A3-768E-46A5-846C-78784B4232A1")]
public sealed class WidgetProvider : IWidgetProvider
{
    private static readonly WidgetStateStore State = new();
    private static readonly CardRenderer Renderer = new();

    public void CreateWidget(WidgetContext widgetContext)
    {
        var widgetId = widgetContext.Id;
        var kind = widgetContext.DefinitionId;
        var size = SizeToString(widgetContext.Size);

        State.Add(widgetId, kind, size);
        UpdateWidget(widgetId, kind, size);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        State.Remove(widgetId);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var widgetId = actionInvokedArgs.WidgetContext.Id;
        var info = State.Get(widgetId);
        if (info is null) return;
        UpdateWidget(widgetId, info.Kind, info.Size);
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var widgetId = contextChangedArgs.WidgetContext.Id;
        var newSize = SizeToString(contextChangedArgs.WidgetContext.Size);
        State.UpdateSize(widgetId, newSize);
        var info = State.Get(widgetId);
        if (info is null) return;
        UpdateWidget(widgetId, info.Kind, newSize);
    }

    public void Activate(WidgetContext widgetContext)
    {
        // v1 = no-op. Already push on CreateWidget / OnActionInvoked / OnWidgetContextChanged.
    }

    public void Deactivate(string widgetId)
    {
        // v1 = no-op.
    }

    private static string SizeToString(WidgetSize size) => size switch
    {
        WidgetSize.Small => "small",
        WidgetSize.Medium => "medium",
        WidgetSize.Large => "large",
        _ => "small",
    };

    private static void UpdateWidget(string widgetId, string kind, string size)
    {
        try
        {
            var snapshot = SnapshotFetcher.GetCurrent();
            var json = Renderer.Render(kind, size, snapshot);

            var update = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = json,
                Data = "{}",
            };
            WidgetManager.GetDefault().UpdateWidget(update);
        }
        catch (Exception ex)
        {
            try
            {
                var fallback = CardRenderer.RenderError(ex.Message);
                var update = new WidgetUpdateRequestOptions(widgetId)
                {
                    Template = fallback,
                    Data = "{}",
                };
                WidgetManager.GetDefault().UpdateWidget(update);
            }
            catch
            {
                // Truly catastrophic — give up silently.
            }
        }
    }
}
