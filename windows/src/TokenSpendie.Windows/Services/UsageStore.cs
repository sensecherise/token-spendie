using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

/// <summary>
/// Drives polling of every registered provider, owns the per-provider state
/// and surfaces it as <see cref="Providers"/> through <see cref="INotifyPropertyChanged"/>.
/// </summary>
public sealed class UsageStore : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan ManualRefreshMinGap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FreshCacheWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] BackoffSteps =
        { TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15) };

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ProviderUsage> Providers { get; private set; } =
        System.Array.Empty<ProviderUsage>();
    public bool IsRefreshing { get; private set; }

    /// <summary>The provider whose ring drives the tray icon. Uses the
    /// preferences pick when that provider is detected; otherwise the first
    /// detected provider; otherwise null.</summary>
    public ProviderUsage? MenuBarProvider()
    {
        var preferred = _preferences?.MenuBarProviderID;
        if (preferred is { } pref)
        {
            foreach (var u in Providers)
                if (u.Id == pref) return u;
        }
        return Providers.Count > 0 ? Providers[0] : null;
    }

    private readonly IUsageProvider[] _registered;
    private readonly Dictionary<ProviderID, SnapshotCache> _caches;
    private readonly Func<DateTimeOffset> _now;
    private readonly PreferencesStore? _preferences;
    private readonly INetworkAvailabilityObserver? _network;
    private readonly IPowerEventObserver? _power;

    private readonly Dictionary<ProviderID, ProviderUsage> _usageByID = new();
    private readonly Dictionary<ProviderID, DateTimeOffset> _backoffUntil = new();
    private readonly Dictionary<ProviderID, int> _consecutiveRateLimits = new();
    private readonly Dictionary<ProviderID, DateTimeOffset> _lastSuccess = new();

    private DateTimeOffset? _lastManualRefresh;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public UsageStore(
        IEnumerable<IUsageProvider> providers,
        Func<ProviderID, SnapshotCache>? cacheFactory = null,
        Func<DateTimeOffset>? now = null,
        PreferencesStore? preferences = null,
        INetworkAvailabilityObserver? network = null,
        IPowerEventObserver? power = null)
    {
        _registered = providers.ToArray();
        cacheFactory ??= id => new SnapshotCache(SnapshotCache.DefaultPathFor(id));
        _caches = _registered.ToDictionary(p => p.Id, p => cacheFactory(p.Id));
        _now = now ?? (() => DateTimeOffset.Now);
        _preferences = preferences;
        _network = network;
        _power = power;

        if (_network is not null) _network.Reconnected += OnNetworkReconnected;
        if (_power is not null) _power.Resumed += OnPowerResumed;
    }

    private void OnNetworkReconnected(object? sender, EventArgs e) => _ = RefreshAsync();
    private void OnPowerResumed(object? sender, EventArgs e) => _ = RefreshAsync();

    /// <summary>Loads cached snapshots, starts the polling timer.</summary>
    public void Start()
    {
        foreach (var provider in _registered)
        {
            var cached = _caches[provider.Id].Load();
            if (cached is not null)
            {
                var fresh = (_now() - cached.FetchedAt) < FreshCacheWindow;
                _usageByID[provider.Id] = new ProviderUsage(provider.Id, provider.DisplayName)
                {
                    State = fresh ? LoadState.Ok : LoadState.Stale,
                    Snapshot = cached,
                };
                if (fresh) _lastSuccess[provider.Id] = cached.FetchedAt;
            }
            else
            {
                _usageByID[provider.Id] = new ProviderUsage(provider.Id, provider.DisplayName)
                {
                    State = LoadState.Loading,
                };
            }
        }
        Publish(_registered.Select(p => p.Id));

        _cts = new CancellationTokenSource();
        var interval = _preferences?.RefreshInterval.AsTimeSpan() ?? TimeSpan.FromSeconds(60);
        _timer = new PeriodicTimer(interval);
        _loopTask = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                MarkStaleIfNeeded();
                await RefreshAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    /// <summary>One refresh cycle. Detect every registered provider, then fetch
    /// each detected one (skipping those currently in 429 backoff unless ignoringBackoff).</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await RunCycleAsync(ignoringBackoff: false, ct).ConfigureAwait(false);
    }

    /// <summary>User-initiated refresh from the tray menu. Skips backoff. Ignored
    /// while a refresh is already running or within 2s of the previous manual refresh.</summary>
    public async Task ManualRefreshAsync(CancellationToken ct = default)
    {
        if (IsRefreshing) return;
        if (_lastManualRefresh is { } last && (_now() - last) < ManualRefreshMinGap) return;
        _lastManualRefresh = _now();
        await RunCycleAsync(ignoringBackoff: true, ct).ConfigureAwait(false);
    }

    private async Task RunCycleAsync(bool ignoringBackoff, CancellationToken ct)
    {
        IsRefreshing = true;
        Raise(nameof(IsRefreshing));
        try
        {
            var detected = _registered.Where(p => p.DetectCredentials()).ToArray();
            var detectedIds = detected.Select(p => p.Id).ToHashSet();

            // Drop entries for providers no longer detected.
            foreach (var id in _usageByID.Keys.Except(detectedIds).ToArray())
                _usageByID.Remove(id);

            foreach (var provider in detected)
                await RefreshOneAsync(provider, ignoringBackoff, ct).ConfigureAwait(false);

            Publish(detected.Select(p => p.Id));
        }
        finally
        {
            IsRefreshing = false;
            Raise(nameof(IsRefreshing));
        }
    }

    private async Task RefreshOneAsync(IUsageProvider provider, bool ignoringBackoff, CancellationToken ct)
    {
        var id = provider.Id;
        if (!ignoringBackoff && _backoffUntil.TryGetValue(id, out var until) && _now() < until)
            return;

        _usageByID.TryAdd(id, new ProviderUsage(id, provider.DisplayName) { State = LoadState.Loading });

        try
        {
            var snapshot = await provider.FetchUsageAsync(ct).ConfigureAwait(false);
            ApplySuccess(snapshot, id, provider.DisplayName);
        }
        catch (ProviderUnauthorizedException)
        {
            SetState(LoadState.Error(UsageErrorKind.LoginExpired), id);
        }
        catch (ProviderNetworkException)
        {
            Degrade(UsageErrorKind.Network, id);
        }
        catch (ProviderBadResponseException)
        {
            Degrade(UsageErrorKind.BadResponse, id);
        }
        catch (ProviderRateLimitedException ex)
        {
            ApplyRateLimitBackoff(ex.RetryAfter, id);
        }
        catch (CredentialNotFoundException)
        {
            SetState(LoadState.Error(UsageErrorKind.ClaudeCodeNotFound), id);
        }
        catch (CredentialMalformedException)
        {
            SetState(LoadState.Error(UsageErrorKind.ClaudeCodeNotFound), id);
        }
        catch (CredentialAccessDeniedException)
        {
            SetState(LoadState.Error(UsageErrorKind.CredentialAccessDenied), id);
        }
        catch
        {
            Degrade(UsageErrorKind.BadResponse, id);
        }
    }

    private void ApplySuccess(ProviderSnapshot snapshot, ProviderID id, string displayName)
    {
        _usageByID[id] = new ProviderUsage(id, displayName)
        {
            State = LoadState.Ok,
            Snapshot = snapshot,
        };
        _caches[id].Save(snapshot);
        _lastSuccess[id] = _now();
        _backoffUntil.Remove(id);
        _consecutiveRateLimits.Remove(id);
    }

    private void SetState(LoadState state, ProviderID id)
    {
        if (!_usageByID.TryGetValue(id, out var usage)) return;
        usage.State = state;
    }

    /// <summary>Soft failure: keep the cached snapshot as stale if there is one,
    /// otherwise surface the error.</summary>
    private void Degrade(UsageErrorKind kind, ProviderID id)
    {
        if (_usageByID.TryGetValue(id, out var usage) && usage.Snapshot is not null)
            usage.State = LoadState.Stale;
        else
            SetState(LoadState.Error(kind), id);
    }

    private void ApplyRateLimitBackoff(TimeSpan? retryAfter, ProviderID id)
    {
        var count = _consecutiveRateLimits.TryGetValue(id, out var c) ? c + 1 : 1;
        _consecutiveRateLimits[id] = count;
        var fallback = BackoffSteps[System.Math.Min(count - 1, BackoffSteps.Length - 1)];
        _backoffUntil[id] = _now() + (retryAfter ?? fallback);
        Degrade(UsageErrorKind.BadResponse, id);
    }

    private void MarkStaleIfNeeded()
    {
        var interval = _preferences?.RefreshInterval.AsTimeSpan() ?? TimeSpan.FromSeconds(60);
        var threshold = interval * 3;
        foreach (var (id, usage) in _usageByID.ToArray())
        {
            if (usage.State != LoadState.Ok) continue;
            if (_lastSuccess.TryGetValue(id, out var last) && (_now() - last) > threshold)
                usage.State = LoadState.Stale;
        }
    }

    private void Publish(IEnumerable<ProviderID> order)
    {
        Providers = order
            .Select(id => _usageByID.TryGetValue(id, out var u) ? u : null)
            .Where(u => u is not null)
            .Cast<ProviderUsage>()
            .ToArray();
        Raise(nameof(Providers));
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public async ValueTask DisposeAsync()
    {
        if (_network is not null) _network.Reconnected -= OnNetworkReconnected;
        if (_power is not null) _power.Resumed -= OnPowerResumed;
        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); } catch { }
        }
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
