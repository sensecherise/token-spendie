import Foundation
import Combine
import Network
import AppKit

/// Owns polling, the data pipeline, and the published widget state.
@MainActor
final class UsageStore: ObservableObject {
    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var state: LoadState = .loading
    /// True while a refresh cycle is running. Drives the refresh icon's spin
    /// animation and disables the button so it cannot be spammed.
    @Published private(set) var isRefreshing = false

    private let provider: UsageProvider
    private let credentials: CredentialStore
    private let cache: SnapshotCache
    private let preferences: Preferences
    private let now: () -> Date

    private var timer: Timer?
    private var lastSuccess: Date?
    /// While set and in the future, polling is paused — set after an HTTP 429.
    private var backoffUntil: Date?
    private var consecutiveRateLimits = 0
    /// Timestamp of the last user-initiated refresh. Manual refreshes within
    /// `manualRefreshMinGap` of this are ignored, keeping the button un-spammable.
    private var lastManualRefresh: Date?
    private static let manualRefreshMinGap: TimeInterval = 2
    private let pathMonitor = NWPathMonitor()
    /// Last known network reachability, used to detect reconnect transitions.
    /// Main-actor isolated — only touched inside the `@MainActor` task below.
    private var networkWasSatisfied = true

    init(provider: UsageProvider,
         credentials: CredentialStore,
         cache: SnapshotCache,
         preferences: Preferences,
         now: @escaping () -> Date = Date.init) {
        self.provider = provider
        self.credentials = credentials
        self.cache = cache
        self.preferences = preferences
        self.now = now
    }

    /// Loads the cached snapshot, begins polling, observes wake/network, and fires an initial fetch.
    func start() {
        if let cached = cache.load() {
            snapshot = cached
            // A recently-cached snapshot counts as live, so a relaunch needs no
            // immediate fetch — this avoids request bursts across restarts.
            if now().timeIntervalSince(cached.fetchedAt) < 60 {
                state = .ok
                lastSuccess = cached.fetchedAt
            } else {
                state = .stale
            }
        }
        NotificationCenter.default.addObserver(
            self, selector: #selector(systemDidWake),
            name: NSWorkspace.didWakeNotification, object: nil)
        observeNetwork()
        rescheduleTimer()
        if state != .ok {
            Task { await refreshNow() }
        }
    }

    /// Performs one refresh cycle: load credentials, fetch, retry once on 401.
    /// `ignoringBackoff` lets a user-initiated refresh proceed during 429
    /// backoff; automatic callers leave it `false` so polling stays paused.
    func refreshNow(ignoringBackoff: Bool = false) async {
        if !ignoringBackoff, let backoffUntil, now() < backoffUntil { return }
        isRefreshing = true
        defer { isRefreshing = false }
        if snapshot == nil { state = .loading }
        do {
            let creds = try credentials.loadCredentials()
            do {
                apply(try await provider.fetchUsage(accessToken: creds.accessToken))
            } catch ProviderError.unauthorized {
                // Re-read the Keychain once — Claude Code refreshes the token during normal use.
                let refreshed = try credentials.loadCredentials()
                apply(try await provider.fetchUsage(accessToken: refreshed.accessToken))
            }
        } catch ProviderError.unauthorized {
            state = .error(.loginExpired)
        } catch ProviderError.network {
            degrade(to: .network)
        } catch ProviderError.badResponse {
            degrade(to: .badResponse)
        } catch ProviderError.rateLimited(let retryAfter) {
            applyRateLimitBackoff(retryAfter: retryAfter)
        } catch CredentialError.notFound, CredentialError.malformed {
            state = .error(.claudeCodeNotFound)
        } catch CredentialError.accessDenied {
            state = .error(.keychainAccessDenied)
        } catch {
            degrade(to: .badResponse)
        }
    }

    /// A user-initiated refresh from the refresh button. Ignored while a refresh
    /// is already running, or if the previous manual refresh was under
    /// `manualRefreshMinGap` seconds ago — together these keep the button
    /// un-spammable. Other refresh triggers (timer, wake, reconnect) bypass this
    /// and call `refreshNow()` directly.
    func manualRefresh() async {
        if isRefreshing { return }
        if let lastManualRefresh,
           now().timeIntervalSince(lastManualRefresh) < Self.manualRefreshMinGap {
            return
        }
        // Stamped before the await, on purpose: the time-gap guard and the
        // `isRefreshing` guard above are independent. A repeat call landing
        // while this refresh is still in flight is dropped by `isRefreshing`,
        // not the gap — both guards are needed to keep the button un-spammable.
        lastManualRefresh = now()
        // Bypass 429 backoff: the user is explicitly asking, the endpoint may
        // have recovered, and the 2s gap above already caps the request rate.
        await refreshNow(ignoringBackoff: true)
    }

    /// When the endpoint is rate-limiting us (HTTP 429), the time the limit
    /// resets; `nil` otherwise. Lets the status line show "rate limited"
    /// instead of "offline" — a 429 is not an outage.
    var rateLimitedUntil: Date? {
        guard let backoffUntil, now() < backoffUntil else { return nil }
        return backoffUntil
    }

    /// Re-applies the poll interval after a preference change. The interval is
    /// always the configured value — panel visibility no longer tightens it.
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

    private func apply(_ fresh: UsageSnapshot) {
        snapshot = fresh
        cache.save(fresh)
        lastSuccess = now()
        backoffUntil = nil
        consecutiveRateLimits = 0
        state = .ok
    }

    /// Pauses polling after a 429: honors `Retry-After`, else backs off
    /// exponentially (2m, 5m, 15m). The cached snapshot stays shown as stale.
    private func applyRateLimitBackoff(retryAfter: TimeInterval?) {
        consecutiveRateLimits += 1
        let steps: [TimeInterval] = [120, 300, 900]
        let fallback = steps[min(consecutiveRateLimits - 1, steps.count - 1)]
        backoffUntil = now().addingTimeInterval(retryAfter ?? fallback)
        degrade(to: .badResponse)
    }

    /// A soft failure: keep showing the cached snapshot if we have one.
    private func degrade(to kind: UsageError) {
        if snapshot != nil {
            state = .stale
        } else {
            state = .error(kind)
        }
    }

    /// If a refresh has not succeeded in 3x the poll interval, mark the snapshot stale.
    private func markStaleIfNeeded() {
        guard state == .ok, let lastSuccess else { return }
        let threshold = preferences.refreshInterval.seconds * 3
        if now().timeIntervalSince(lastSuccess) > threshold {
            state = .stale
        }
    }

    @objc private func systemDidWake() {
        Task { await refreshNow() }
    }

    private func observeNetwork() {
        // The handler runs on a background queue. It captures only `[weak self]`
        // and hops to the main actor before touching any state, so there is no
        // shared mutable variable crossing threads.
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
