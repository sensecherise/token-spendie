import XCTest
@testable import TokenSpendie

@MainActor
final class UsageStoreTests: XCTestCase {
    // --- Test doubles ---
    final class StubProvider: UsageProvider {
        var results: [Result<UsageSnapshot, Error>]
        var callCount = 0
        init(_ results: [Result<UsageSnapshot, Error>]) { self.results = results }
        func fetchUsage(accessToken: String) async throws -> UsageSnapshot {
            defer { callCount += 1 }
            return try results[min(callCount, results.count - 1)].get()
        }
    }
    /// A provider that runs a probe closure when `fetchUsage` is called,
    /// for observing store state mid-refresh.
    final class ProbeProvider: UsageProvider {
        var onFetch: () -> Void = {}
        let result: Result<UsageSnapshot, Error>
        init(_ result: Result<UsageSnapshot, Error>) { self.result = result }
        func fetchUsage(accessToken: String) async throws -> UsageSnapshot {
            onFetch()
            return try result.get()
        }
    }

    private func snapshot(_ percent: Double, at: TimeInterval = 0) -> UsageSnapshot {
        UsageSnapshot(session: UsageWindow(percent: percent, resetsAt: nil),
                      weekly: UsageWindow(percent: percent, resetsAt: nil),
                      modelWeeklies: [], fetchedAt: Date(timeIntervalSince1970: at))
    }

    /// A `TokenStore` backed by a throwaway UserDefaults suite. Pass `token: nil`
    /// for the no-token case.
    private func tokenStore(token: String? = "tok") -> TokenStore {
        let store = TokenStore(defaults: UserDefaults(suiteName: "UsageStoreTests-\(UUID().uuidString)")!)
        if let token { try? store.save(token) }
        return store
    }

    private func makeStore(tokenStore: TokenStore, provider: UsageProvider,
                           now: @escaping () -> Date = { Date(timeIntervalSince1970: 0) }) -> UsageStore {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("store-\(UUID().uuidString).json")
        return UsageStore(provider: provider,
                          tokenStore: tokenStore,
                          cache: SnapshotCache(fileURL: url),
                          preferences: Preferences(defaults: UserDefaults(suiteName: UUID().uuidString)!),
                          now: now)
    }

    func testSuccessfulRefreshPublishesSnapshotAndOk() async {
        let store = makeStore(tokenStore: tokenStore(),
                              provider: StubProvider([.success(snapshot(42))]))
        await store.refreshNow()
        XCTAssertEqual(store.snapshot?.session.percent, 42)
        XCTAssertEqual(store.state, .ok)
    }

    func testNoTokenSurfacesNoTokenAndMakesNoRequest() async {
        let provider = StubProvider([.success(snapshot(1))])
        let store = makeStore(tokenStore: tokenStore(token: nil), provider: provider)
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.noToken))
        XCTAssertEqual(provider.callCount, 0, "no request is made without a token")
    }

    func testUnauthorizedSurfacesTokenInvalid() async {
        let store = makeStore(tokenStore: tokenStore(),
                              provider: StubProvider([.failure(ProviderError.unauthorized)]))
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.tokenInvalid))
    }

    func testUnauthorizedDoesNotRetry() async {
        let provider = StubProvider([.failure(ProviderError.unauthorized),
                                     .success(snapshot(55))])
        let store = makeStore(tokenStore: tokenStore(), provider: provider)
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.tokenInvalid))
        XCTAssertEqual(provider.callCount, 1, "a 401 is not retried")
    }

    func testNetworkFailureWithCachedSnapshotGoesStale() async {
        let provider = StubProvider([.success(snapshot(30)), .failure(ProviderError.network)])
        let store = makeStore(tokenStore: tokenStore(), provider: provider)
        await store.refreshNow()   // succeeds
        await store.refreshNow()   // fails
        XCTAssertEqual(store.state, .stale)
        XCTAssertEqual(store.snapshot?.session.percent, 30, "keeps the last good snapshot")
    }

    func testNetworkFailureWithNoSnapshotSurfacesError() async {
        let store = makeStore(tokenStore: tokenStore(),
                              provider: StubProvider([.failure(ProviderError.network)]))
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.network))
    }

    func testRateLimitPausesPolling() async {
        let provider = StubProvider([.success(snapshot(30)),
                                     .failure(ProviderError.rateLimited(retryAfter: 600))])
        let store = makeStore(tokenStore: tokenStore(), provider: provider,
                              now: { Date(timeIntervalSince1970: 0) })
        await store.refreshNow()   // success — snapshot cached
        await store.refreshNow()   // 429 — backoff begins
        XCTAssertEqual(store.state, .stale)
        let callsAfter429 = provider.callCount
        await store.refreshNow()   // within the backoff window — must be skipped
        XCTAssertEqual(provider.callCount, callsAfter429, "polling is paused during backoff")
    }

    func testIsRefreshingTrueDuringFetchAndFalseAfter() async {
        let provider = ProbeProvider(.success(snapshot(20)))
        let store = makeStore(tokenStore: tokenStore(), provider: provider)
        var observedDuringFetch = false
        provider.onFetch = { observedDuringFetch = store.isRefreshing }
        XCTAssertFalse(store.isRefreshing, "idle before any refresh")
        await store.refreshNow()
        XCTAssertTrue(observedDuringFetch, "isRefreshing is true while the fetch runs")
        XCTAssertFalse(store.isRefreshing, "isRefreshing clears after the refresh")
    }

    func testIsRefreshingClearsWhenFetchThrows() async {
        let store = makeStore(tokenStore: tokenStore(),
                              provider: StubProvider([.failure(ProviderError.network)]))
        await store.refreshNow()
        XCTAssertFalse(store.isRefreshing, "isRefreshing clears even when the fetch fails")
    }

    func testIsRefreshingClearsOnNoTokenEarlyReturn() async {
        let store = makeStore(tokenStore: tokenStore(token: nil),
                              provider: StubProvider([.success(snapshot(1))]))
        await store.refreshNow()
        XCTAssertFalse(store.isRefreshing, "isRefreshing clears on the no-token early return")
    }

    func testManualRefreshIgnoresRapidRepeatCalls() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider([.success(snapshot(10)), .success(snapshot(10))])
        let store = makeStore(tokenStore: tokenStore(), provider: provider, now: { clock })
        await store.manualRefresh()                       // fires
        await store.manualRefresh()                       // within 2s gap — skipped
        XCTAssertEqual(provider.callCount, 1, "a second manual refresh within 2s is ignored")
        clock = Date(timeIntervalSince1970: 3)            // past the gap
        await store.manualRefresh()                       // fires again
        XCTAssertEqual(provider.callCount, 2, "a manual refresh after the gap runs")
    }

    func testManualRefreshBypassesRateLimitBackoff() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider([.success(snapshot(30)),
                                     .failure(ProviderError.rateLimited(retryAfter: 600)),
                                     .success(snapshot(45))])
        let store = makeStore(tokenStore: tokenStore(), provider: provider, now: { clock })
        await store.manualRefresh()                       // success
        clock = Date(timeIntervalSince1970: 3)
        await store.manualRefresh()                       // 429 — backoff begins
        let callsAfter429 = provider.callCount
        clock = Date(timeIntervalSince1970: 6)
        await store.manualRefresh()                       // backoff active — a manual refresh still fetches
        XCTAssertEqual(provider.callCount, callsAfter429 + 1,
                       "a manual refresh bypasses 429 backoff")
        XCTAssertEqual(store.snapshot?.session.percent, 45,
                       "the bypassing refresh applied fresh data")
        XCTAssertEqual(store.state, .ok,
                       "a successful manual refresh clears the degraded state")
    }

    func testAutomaticRefreshStillHonorsRateLimitBackoff() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider([.success(snapshot(30)),
                                     .failure(ProviderError.rateLimited(retryAfter: 600))])
        let store = makeStore(tokenStore: tokenStore(), provider: provider, now: { clock })
        await store.refreshNow()                          // success
        clock = Date(timeIntervalSince1970: 3)
        await store.refreshNow()                          // 429 — backoff begins
        let callsAfter429 = provider.callCount
        clock = Date(timeIntervalSince1970: 6)
        await store.refreshNow()                          // automatic — must stay paused
        XCTAssertEqual(provider.callCount, callsAfter429,
                       "an automatic refresh during backoff makes no request")
    }

    func testRateLimitedUntilReflectsBackoff() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider([.success(snapshot(30)),
                                     .failure(ProviderError.rateLimited(retryAfter: 600)),
                                     .success(snapshot(40))])
        let store = makeStore(tokenStore: tokenStore(), provider: provider, now: { clock })
        await store.refreshNow()
        XCTAssertNil(store.rateLimitedUntil, "not rate limited after a success")
        clock = Date(timeIntervalSince1970: 1)
        await store.refreshNow()                          // 429
        XCTAssertEqual(store.rateLimitedUntil, Date(timeIntervalSince1970: 601),
                       "rate limited until now + Retry-After")
        clock = Date(timeIntervalSince1970: 2)
        await store.manualRefresh()                       // bypasses backoff, succeeds
        XCTAssertNil(store.rateLimitedUntil, "cleared after a successful refresh")
    }
}
