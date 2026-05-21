import Foundation
import Combine
import Network
import AppKit

/// A display surface that can hold the detail view open.
enum PanelSource {
    case menuBar
    case floating
}

/// Owns polling, the data pipeline, and the published widget state.
@MainActor
final class UsageStore: ObservableObject {
    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var state: LoadState = .loading

    private let provider: UsageProvider
    private let credentials: CredentialStore
    private let cache: SnapshotCache
    private let preferences: Preferences
    private let now: () -> Date

    private var timer: Timer?
    /// The display surfaces currently open. The poll interval tightens while any
    /// surface is visible, so visibility is tracked per source — closing one
    /// surface must not slow polling while another is still open.
    private var visiblePanels: Set<PanelSource> = []
    private var panelVisible: Bool { !visiblePanels.isEmpty }
    private var lastSuccess: Date?
    private let pathMonitor = NWPathMonitor()
    /// Last known network reachability, used to detect reconnect transitions.
    /// Main-actor isolated — only touched inside the `@MainActor` task below.
    private var networkWasSatisfied = true
    private static let panelOpenInterval: TimeInterval = 20

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
            state = .stale   // cached data is not yet confirmed live
        }
        NotificationCenter.default.addObserver(
            self, selector: #selector(systemDidWake),
            name: NSWorkspace.didWakeNotification, object: nil)
        observeNetwork()
        rescheduleTimer()
        Task { await refreshNow() }
    }

    /// Performs one refresh cycle: load credentials, fetch, retry once on 401.
    func refreshNow() async {
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
        } catch CredentialError.notFound, CredentialError.malformed {
            state = .error(preferences.credentialMode == .manual ? .noManualToken : .claudeCodeNotFound)
        } catch CredentialError.accessDenied {
            state = .error(.keychainAccessDenied)
        } catch {
            degrade(to: .badResponse)
        }
    }

    /// Call when a display surface opens or closes; the poll interval tightens
    /// while any surface is open. Tracked per source so closing one surface does
    /// not slow polling while another is still visible. Idempotent per source.
    func setPanelVisible(_ visible: Bool, source: PanelSource) {
        let wasVisible = panelVisible
        if visible { visiblePanels.insert(source) } else { visiblePanels.remove(source) }
        guard panelVisible != wasVisible else { return }
        rescheduleTimer()
        if panelVisible { Task { await refreshNow() } }
    }

    /// Re-applies the poll interval after a preference change.
    func rescheduleTimer() {
        timer?.invalidate()
        let interval = panelVisible ? Self.panelOpenInterval : preferences.refreshInterval.seconds
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
        state = .ok
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
        pathMonitor.start(queue: DispatchQueue(label: "ClaudeUsage.network"))
    }

    deinit {
        pathMonitor.cancel()
    }
}
