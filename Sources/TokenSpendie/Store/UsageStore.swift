import Foundation
import Combine
import Network
import AppKit

/// Owns polling, the per-provider data pipeline, and the published widget
/// state. Every registered `UsageProvider` is detected and polled
/// independently; one provider failing never blocks another.
@MainActor
final class UsageStore: ObservableObject {
    /// One row per detected provider, in registration order.
    @Published private(set) var providers: [ProviderUsage] = []
    /// The provider whose ring rides the menu bar.
    @Published private(set) var menuBarProviderID: ProviderID
    /// True while any provider's refresh cycle is running.
    @Published private(set) var isRefreshing = false

    private let registered: [UsageProvider]
    private let caches: [ProviderID: SnapshotCache]
    private let preferences: Preferences
    private let now: () -> Date

    /// Per-provider runtime bookkeeping, keyed by provider id.
    private var usageByID: [ProviderID: ProviderUsage] = [:]
    private var backoffUntil: [ProviderID: Date] = [:]
    private var consecutiveRateLimits: [ProviderID: Int] = [:]
    private var lastSuccess: [ProviderID: Date] = [:]

    private var timer: Timer?
    private var lastManualRefresh: Date?
    private static let manualRefreshMinGap: TimeInterval = 2
    private let pathMonitor = NWPathMonitor()
    private var networkWasSatisfied = true

    init(providers: [UsageProvider],
         cacheFactory: (ProviderID) -> SnapshotCache = { SnapshotCache(fileURL: SnapshotCache.defaultURL(for: $0)) },
         preferences: Preferences,
         now: @escaping () -> Date = Date.init) {
        self.registered = providers
        self.caches = Dictionary(uniqueKeysWithValues: providers.map { ($0.id, cacheFactory($0.id)) })
        self.preferences = preferences
        self.now = now
        self.menuBarProviderID = preferences.menuBarProviderID
    }

    /// The detected provider shown in the menu bar — the stored choice if it is
    /// still detected, else the first detected provider.
    var menuBarProvider: ProviderUsage? {
        providers.first { $0.id == menuBarProviderID } ?? providers.first
    }

    /// Picks the provider whose ring rides the menu bar; persists the choice.
    func setMenuBarProvider(_ id: ProviderID) {
        menuBarProviderID = id
        preferences.menuBarProviderID = id
    }

    /// When a provider is in 429 backoff, the time the limit resets; else nil.
    func rateLimitedUntil(for id: ProviderID) -> Date? {
        guard let until = backoffUntil[id], now() < until else { return nil }
        return until
    }

    /// Loads cached snapshots, begins polling, observes wake/network, fires an
    /// initial fetch.
    func start() {
        for provider in registered {
            if let cached = caches[provider.id]?.load() {
                let fresh = now().timeIntervalSince(cached.fetchedAt) < 60
                usageByID[provider.id] = ProviderUsage(
                    id: provider.id, displayName: provider.displayName,
                    state: fresh ? .ok : .stale, snapshot: cached)
                if fresh { lastSuccess[provider.id] = cached.fetchedAt }
            }
        }
        // Publish cached rows immediately so launch has no empty flash.
        publish(order: registered.map(\.id))
        NotificationCenter.default.addObserver(
            self, selector: #selector(systemDidWake),
            name: NSWorkspace.didWakeNotification, object: nil)
        observeNetwork()
        rescheduleTimer()
        // Skip the initial fetch when every provider already has a fresh
        // cached snapshot — avoids a request burst on every relaunch.
        let needsImmediateFetch = registered.contains { usageByID[$0.id]?.state != .ok }
        if needsImmediateFetch {
            Task { await refreshNow() }
        }
    }

    /// One refresh cycle: detect every registered provider, then fetch each
    /// detected provider. `ignoringBackoff` lets a user-initiated refresh
    /// proceed during 429 backoff.
    func refreshNow(ignoringBackoff: Bool = false) async {
        isRefreshing = true
        defer { isRefreshing = false }

        let detected = registered.filter { $0.detectCredentials() }
        let detectedIDs = Set(detected.map(\.id))
        // Drop rows for providers no longer detected.
        usageByID = usageByID.filter { detectedIDs.contains($0.key) }

        // Sequential in Phase 1 (one provider). Phase 2 introduces concurrent
        // fetches once a second provider exists.
        for provider in detected {
            await refresh(provider, ignoringBackoff: ignoringBackoff)
        }
        publish(order: detected.map(\.id))
    }

    /// A user-initiated refresh from the refresh button. Refreshes every
    /// provider, bypassing 429 backoff. Ignored while a refresh is already
    /// running or within `manualRefreshMinGap` of the previous manual refresh.
    func manualRefresh() async {
        if isRefreshing { return }
        if let last = lastManualRefresh,
           now().timeIntervalSince(last) < Self.manualRefreshMinGap { return }
        lastManualRefresh = now()
        await refreshNow(ignoringBackoff: true)
    }

    /// Re-applies the poll interval after a preference change.
    func rescheduleTimer() {
        timer?.invalidate()
        let interval = preferences.refreshInterval.seconds
        let timer = Timer(timeInterval: interval, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.markStaleIfNeeded()
                await self?.refreshNow()
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    // MARK: - Private

    /// Refreshes one provider, mapping success/failure onto its `ProviderUsage`.
    private func refresh(_ provider: UsageProvider, ignoringBackoff: Bool) async {
        let id = provider.id
        if !ignoringBackoff, let until = backoffUntil[id], now() < until { return }

        if usageByID[id] == nil {
            usageByID[id] = ProviderUsage(id: id, displayName: provider.displayName,
                                          state: .loading, snapshot: nil)
        }
        do {
            apply(try await provider.fetchUsage(), for: id, displayName: provider.displayName)
        } catch ProviderError.unauthorized {
            setState(.error(.loginExpired), for: id)
        } catch ProviderError.network {
            degrade(to: .network, for: id)
        } catch ProviderError.badResponse {
            degrade(to: .badResponse, for: id)
        } catch ProviderError.rateLimited(let retryAfter) {
            applyRateLimitBackoff(retryAfter: retryAfter, for: id)
        } catch CredentialError.notFound, CredentialError.malformed {
            setState(.error(.claudeCodeNotFound), for: id)
        } catch CredentialError.accessDenied {
            setState(.error(.keychainAccessDenied), for: id)
        } catch {
            degrade(to: .badResponse, for: id)
        }
    }

    private func apply(_ snapshot: ProviderSnapshot, for id: ProviderID, displayName: String) {
        usageByID[id] = ProviderUsage(id: id, displayName: displayName,
                                      state: .ok, snapshot: snapshot)
        caches[id]?.save(snapshot)
        lastSuccess[id] = now()
        backoffUntil[id] = nil
        consecutiveRateLimits[id] = 0
    }

    private func setState(_ state: LoadState, for id: ProviderID) {
        guard var usage = usageByID[id] else { return }
        usage.state = state
        usageByID[id] = usage
    }

    /// A soft failure: keep showing the cached snapshot as stale if there is
    /// one, else surface the error.
    private func degrade(to kind: UsageError, for id: ProviderID) {
        if usageByID[id]?.snapshot != nil {
            setState(.stale, for: id)
        } else {
            setState(.error(kind), for: id)
        }
    }

    /// Pauses polling for one provider after a 429: honors `Retry-After`, else
    /// backs off exponentially (2m, 5m, 15m).
    private func applyRateLimitBackoff(retryAfter: TimeInterval?, for id: ProviderID) {
        let count = (consecutiveRateLimits[id] ?? 0) + 1
        consecutiveRateLimits[id] = count
        let steps: [TimeInterval] = [120, 300, 900]
        let fallback = steps[min(count - 1, steps.count - 1)]
        backoffUntil[id] = now().addingTimeInterval(retryAfter ?? fallback)
        degrade(to: .badResponse, for: id)
    }

    /// Marks a provider's snapshot stale if no refresh has succeeded in 3x the
    /// poll interval.
    private func markStaleIfNeeded() {
        let threshold = preferences.refreshInterval.seconds * 3
        for (id, usage) in usageByID where usage.state == .ok {
            if let last = lastSuccess[id], now().timeIntervalSince(last) > threshold {
                setState(.stale, for: id)
            }
        }
        publish(order: providers.map(\.id))
    }

    /// Pushes the internal per-provider map to the published `providers` array,
    /// ordered by the given provider-id order.
    private func publish(order: [ProviderID]) {
        providers = order.compactMap { usageByID[$0] }
    }

    @objc private func systemDidWake() {
        Task { await refreshNow() }
    }

    private func observeNetwork() {
        pathMonitor.pathUpdateHandler = { [weak self] path in
            let satisfied = path.status == .satisfied
            Task { @MainActor in
                guard let self else { return }
                let reconnected = satisfied && !self.networkWasSatisfied
                self.networkWasSatisfied = satisfied
                if reconnected { await self.refreshNow() }
            }
        }
        pathMonitor.start(queue: DispatchQueue(label: "TokenSpendie.network"))
    }

    deinit {
        pathMonitor.cancel()
    }
}
