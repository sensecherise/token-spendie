using System.Collections.Concurrent;

namespace TokenSpendie.WidgetProvider;

public sealed record WidgetInfo(string Kind, string Size);

/// <summary>Per-process, per-widgetId state for active widgets. Thread-safe.</summary>
public sealed class WidgetStateStore
{
    private readonly ConcurrentDictionary<string, WidgetInfo> _byId = new();

    public void Add(string widgetId, string kind, string size) =>
        _byId[widgetId] = new WidgetInfo(kind, size);

    public WidgetInfo? Get(string widgetId) =>
        _byId.TryGetValue(widgetId, out var info) ? info : null;

    public void Remove(string widgetId) => _byId.TryRemove(widgetId, out _);

    public void UpdateSize(string widgetId, string size)
    {
        if (_byId.TryGetValue(widgetId, out var existing))
            _byId[widgetId] = existing with { Size = size };
    }

    public int Count => _byId.Count;
}
