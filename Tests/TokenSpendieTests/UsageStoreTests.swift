import XCTest
@testable import TokenSpendie

@MainActor
final class UsageStoreTests: XCTestCase {

    /// A configurable `UsageProvider` test double.
    final class StubProvider: UsageProvider {
        let id: ProviderID
        let displayName: String
        var detected: Bool
        var results: [Result<ProviderSnapshot, Error>]
        var callCount = 0
        var onFetch: () -> Void = {}

        init(id: ProviderID = .claude, displayName: String = "Claude",
             detected: Bool = true, results: [Result<ProviderSnapshot, Error>]) {
            self.id = id
            self.displayName = displayName
            self.detected = detected
            self.results = results
        }
        func detectCredentials() -> Bool { detected }
        func fetchUsage() async throws -> ProviderSnapshot {
            onFetch()
            defer { callCount += 1 }
            return try results[min(callCount, results.count - 1)].get()
        }
    }

    private func snapshot(_ percent: Double, at: TimeInterval = 0,
                          id: ProviderID = .claude) -> ProviderSnapshot {
        let win = LabeledWindow(label: "Session · 5h", detail: "5-hour window",
                                resetStyle: .countdown,
                                window: UsageWindow(percent: percent, resetsAt: nil))
        return ProviderSnapshot(id: id, plan: nil, headline: win, windows: [win],
                                fetchedAt: Date(timeIntervalSince1970: at))
    }

    private func makeStore(_ providers: [UsageProvider],
                           now: @escaping () -> Date = { Date(timeIntervalSince1970: 0) }) -> UsageStore {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("store-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return UsageStore(
            providers: providers,
            cacheFactory: { id in SnapshotCache(fileURL: dir.appendingPathComponent("\(id.rawValue).json")) },
            preferences: Preferences(defaults: UserDefaults(suiteName: UUID().uuidString)!),
            now: now)
    }

    private func usage(_ store: UsageStore, _ id: ProviderID) -> ProviderUsage? {
        store.providers.first { $0.id == id }
    }

    func testSuccessfulRefreshPublishesProviderSnapshotAndOk() async {
        let store = makeStore([StubProvider(results: [.success(snapshot(42))])])
        await store.refreshNow()
        XCTAssertEqual(usage(store, .claude)?.state, .ok)
        XCTAssertEqual(usage(store, .claude)?.snapshot?.headline.window.percent, 42)
    }

    func testUndetectedProviderProducesNoRow() async {
        let store = makeStore([StubProvider(detected: false, results: [.success(snapshot(1))])])
        await store.refreshNow()
        XCTAssertTrue(store.providers.isEmpty, "an undetected provider has no row")
    }

    func testMissingCredentialsSurfaceClaudeCodeNotFound() async {
        let store = makeStore([StubProvider(results: [.failure(CredentialError.notFound)])])
        await store.refreshNow()
        XCTAssertEqual(usage(store, .claude)?.state, .error(.claudeCodeNotFound))
    }

    func testMalformedCredentialsSurfaceClaudeCodeNotFound() async {
        let store = makeStore([StubProvider(results: [.failure(CredentialError.malformed)])])
        await store.refreshNow()
        XCTAssertEqual(usage(store, .claude)?.state, .error(.claudeCodeNotFound))
    }

    func testKeychainDeniedSurfacesError() async {
        let store = makeStore([StubProvider(results: [.failure(CredentialError.accessDenied)])])
        await store.refreshNow()
        XCTAssertEqual(usage(store, .claude)?.state, .error(.keychainAccessDenied))
    }

    func testPersistentUnauthorizedSurfacesLoginExpired() async {
        let store = makeStore([StubProvider(results: [.failure(ProviderError.unauthorized)])])
        await store.refreshNow()
        XCTAssertEqual(usage(store, .claude)?.state, .error(.loginExpired))
    }

    func testNetworkFailureWithCachedSnapshotGoesStale() async {
        let store = makeStore([StubProvider(results: [.success(snapshot(30)),
                                                      .failure(ProviderError.network)])])
        await store.refreshNow()   // succeeds
        await store.refreshNow()   // fails
        XCTAssertEqual(usage(store, .claude)?.state, .stale)
        XCTAssertEqual(usage(store, .claude)?.snapshot?.headline.window.percent, 30)
    }

    func testNetworkFailureWithNoSnapshotSurfacesError() async {
        let store = makeStore([StubProvider(results: [.failure(ProviderError.network)])])
        await store.refreshNow()
        XCTAssertEqual(usage(store, .claude)?.state, .error(.network))
    }

    func testOneProviderFailingDoesNotAbortAnother() throws {
        throw XCTSkip("Multi-provider isolation — deferred to Phase 2, when a second ProviderID exists")
    }

    func testRateLimitPausesPollingForThatProvider() async {
        let provider = StubProvider(results: [.success(snapshot(30)),
                                              .failure(ProviderError.rateLimited(retryAfter: 600))])
        let store = makeStore([provider])
        await store.refreshNow()   // success
        await store.refreshNow()   // 429 — backoff begins
        XCTAssertEqual(usage(store, .claude)?.state, .stale)
        let callsAfter429 = provider.callCount
        await store.refreshNow()   // within backoff — skipped
        XCTAssertEqual(provider.callCount, callsAfter429, "polling paused during backoff")
    }

    func testManualRefreshBypassesRateLimitBackoff() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider(results: [.success(snapshot(30)),
                                              .failure(ProviderError.rateLimited(retryAfter: 600)),
                                              .success(snapshot(45))])
        let store = makeStore([provider], now: { clock })
        await store.manualRefresh()
        clock = Date(timeIntervalSince1970: 3)
        await store.manualRefresh()                       // 429
        let callsAfter429 = provider.callCount
        clock = Date(timeIntervalSince1970: 6)
        await store.manualRefresh()                       // bypasses backoff
        XCTAssertEqual(provider.callCount, callsAfter429 + 1)
        XCTAssertEqual(usage(store, .claude)?.snapshot?.headline.window.percent, 45)
        XCTAssertEqual(usage(store, .claude)?.state, .ok)
    }

    func testManualRefreshIgnoresRapidRepeatCalls() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider(results: [.success(snapshot(10))])
        let store = makeStore([provider], now: { clock })
        await store.manualRefresh()                       // fires
        await store.manualRefresh()                       // within 2s — skipped
        XCTAssertEqual(provider.callCount, 1)
        clock = Date(timeIntervalSince1970: 3)
        await store.manualRefresh()                       // past the gap — fires
        XCTAssertEqual(provider.callCount, 2)
    }

    func testIsRefreshingTrueDuringFetchAndFalseAfter() async {
        let provider = StubProvider(results: [.success(snapshot(20))])
        let store = makeStore([provider])
        var observed = false
        provider.onFetch = { observed = store.isRefreshing }
        XCTAssertFalse(store.isRefreshing)
        await store.refreshNow()
        XCTAssertTrue(observed, "isRefreshing is true while a fetch runs")
        XCTAssertFalse(store.isRefreshing)
    }

    func testMenuBarProviderDefaultsToFirstDetected() async {
        let store = makeStore([StubProvider(results: [.success(snapshot(5))])])
        await store.refreshNow()
        XCTAssertEqual(store.menuBarProvider?.id, .claude)
    }

    func testMenuBarProviderIsNilWhenNothingDetected() async {
        let store = makeStore([StubProvider(detected: false, results: [.success(snapshot(5))])])
        await store.refreshNow()
        XCTAssertNil(store.menuBarProvider)
    }

    func testRateLimitedUntilReflectsBackoffForProvider() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider(results: [.success(snapshot(30)),
                                              .failure(ProviderError.rateLimited(retryAfter: 600))])
        let store = makeStore([provider], now: { clock })
        await store.refreshNow()
        XCTAssertNil(store.rateLimitedUntil(for: .claude))
        clock = Date(timeIntervalSince1970: 1)
        await store.refreshNow()
        XCTAssertEqual(store.rateLimitedUntil(for: .claude), Date(timeIntervalSince1970: 601))
    }
}
