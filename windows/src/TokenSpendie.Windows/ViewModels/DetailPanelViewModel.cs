using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.ViewModels;

public partial class DetailPanelViewModel : ObservableObject
{
    private readonly UsageStore _store;

    [ObservableProperty]
    private IReadOnlyList<ProviderRowViewModel> _rows = System.Array.Empty<ProviderRowViewModel>();

    [ObservableProperty]
    private string _lastRefreshedLabel = "No data yet";

    public DetailPanelViewModel(UsageStore store)
    {
        _store = store;
        _store.PropertyChanged += OnStoreChanged;
        RecomputeRows();
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UsageStore.Providers)) RecomputeRows();
    }

    private void RecomputeRows()
    {
        Rows = _store.Providers
            .Where(p => p.Snapshot is not null)
            .Select(p => new ProviderRowViewModel(p))
            .ToArray();

        var newest = Rows.Count == 0
            ? (System.DateTimeOffset?)null
            : Rows.Max(r => r.FetchedAt);
        LastRefreshedLabel = FormatLastRefreshed(newest);
    }

    internal static string FormatLastRefreshed(System.DateTimeOffset? when)
    {
        if (when is null) return "No data yet";
        var ago = System.DateTimeOffset.UtcNow - when.Value;
        if (ago < System.TimeSpan.Zero) return "Refreshed just now";
        if (ago < System.TimeSpan.FromMinutes(1)) return "Refreshed just now";
        if (ago < System.TimeSpan.FromHours(1)) return $"Refreshed {(int)ago.TotalMinutes}m ago";
        if (ago < System.TimeSpan.FromDays(1)) return $"Refreshed {(int)ago.TotalHours}h ago";
        return $"Refreshed {(int)ago.TotalDays}d ago";
    }
}

public sealed class ProviderRowViewModel
{
    public ProviderRowViewModel(ProviderUsage usage)
    {
        DisplayName = usage.DisplayName;
        State = usage.State;
        var snapshot = usage.Snapshot!;
        HeadlinePercent = snapshot.Headline.Window.Percent;
        HeadlineLabel = snapshot.Headline.Label;
        HeadlineDetail = snapshot.Headline.Detail;
        Level = UsageLevelExtensions.ForPercent(HeadlinePercent);
        Windows = snapshot.Windows;
        Note = snapshot.Note;
        FetchedAt = snapshot.FetchedAt;
    }

    public string DisplayName { get; }
    public LoadState State { get; }
    public double HeadlinePercent { get; }
    public string HeadlineLabel { get; }
    public string HeadlineDetail { get; }
    public UsageLevel Level { get; }
    public IReadOnlyList<LabeledWindow> Windows { get; }
    public string? Note { get; }
    public System.DateTimeOffset FetchedAt { get; }
}
