import XCTest
@testable import ClaudeUsageWidget

@MainActor
final class UsageStoreTests: XCTestCase {
    // --- Test doubles ---
    final class StubCredentials: CredentialStore {
        var result: Result<OAuthCredentials, Error>
        var loadCount = 0
        init(_ result: Result<OAuthCredentials, Error>) { self.result = result }
        func loadCredentials() throws -> OAuthCredentials {
            loadCount += 1
            return try result.get()
        }
    }
    final class StubProvider: UsageProvider {
        var results: [Result<UsageSnapshot, Error>]
        var callCount = 0
        init(_ results: [Result<UsageSnapshot, Error>]) { self.results = results }
        func fetchUsage(accessToken: String) async throws -> UsageSnapshot {
            defer { callCount += 1 }
            return try results[min(callCount, results.count - 1)].get()
        }
    }

    private func creds() -> OAuthCredentials {
        OAuthCredentials(accessToken: "tok", refreshToken: nil, expiresAt: nil)
    }
    private func snapshot(_ percent: Double, at: TimeInterval = 0) -> UsageSnapshot {
        UsageSnapshot(session: UsageWindow(percent: percent, resetsAt: nil),
                      weekly: UsageWindow(percent: percent, resetsAt: nil),
                      modelWeeklies: [], fetchedAt: Date(timeIntervalSince1970: at))
    }
    private func makeStore(credentials: CredentialStore, provider: UsageProvider,
                           now: @escaping () -> Date = { Date(timeIntervalSince1970: 0) }) -> UsageStore {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("store-\(UUID().uuidString).json")
        return UsageStore(provider: provider,
                          credentials: credentials,
                          cache: SnapshotCache(fileURL: url),
                          preferences: Preferences(defaults: UserDefaults(suiteName: UUID().uuidString)!),
                          now: now)
    }

    func testSuccessfulRefreshPublishesSnapshotAndOk() async {
        let store = makeStore(credentials: StubCredentials(.success(creds())),
                              provider: StubProvider([.success(snapshot(42))]))
        await store.refreshNow()
        XCTAssertEqual(store.snapshot?.session.percent, 42)
        XCTAssertEqual(store.state, .ok)
    }

    func testMissingKeychainSurfacesClaudeCodeNotFound() async {
        let store = makeStore(credentials: StubCredentials(.failure(CredentialError.notFound)),
                              provider: StubProvider([.success(snapshot(1))]))
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.claudeCodeNotFound))
    }

    func testKeychainDeniedSurfacesError() async {
        let store = makeStore(credentials: StubCredentials(.failure(CredentialError.accessDenied)),
                              provider: StubProvider([.success(snapshot(1))]))
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.keychainAccessDenied))
    }

    func testUnauthorizedRetriesByRereadingKeychain() async {
        let credentials = StubCredentials(.success(creds()))
        let provider = StubProvider([.failure(ProviderError.unauthorized), .success(snapshot(55))])
        let store = makeStore(credentials: credentials, provider: provider)
        await store.refreshNow()
        XCTAssertEqual(store.state, .ok)
        XCTAssertEqual(store.snapshot?.session.percent, 55)
        XCTAssertEqual(credentials.loadCount, 2, "should re-read the Keychain once on 401")
    }

    func testPersistentUnauthorizedSurfacesLoginExpired() async {
        let provider = StubProvider([.failure(ProviderError.unauthorized),
                                     .failure(ProviderError.unauthorized)])
        let store = makeStore(credentials: StubCredentials(.success(creds())), provider: provider)
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.loginExpired))
    }

    func testNetworkFailureWithCachedSnapshotGoesStale() async {
        let provider = StubProvider([.success(snapshot(30)), .failure(ProviderError.network)])
        let store = makeStore(credentials: StubCredentials(.success(creds())), provider: provider)
        await store.refreshNow()   // succeeds
        await store.refreshNow()   // fails
        XCTAssertEqual(store.state, .stale)
        XCTAssertEqual(store.snapshot?.session.percent, 30, "keeps the last good snapshot")
    }

    func testNetworkFailureWithNoSnapshotSurfacesError() async {
        let store = makeStore(credentials: StubCredentials(.success(creds())),
                              provider: StubProvider([.failure(ProviderError.network)]))
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.network))
    }

    func testManualModeMissingTokenSurfacesNoManualToken() async {
        let prefsDefaults = UserDefaults(suiteName: UUID().uuidString)!
        let preferences = Preferences(defaults: prefsDefaults)
        preferences.credentialMode = .manual
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("store-\(UUID().uuidString).json")
        let store = UsageStore(
            provider: StubProvider([.success(snapshot(1))]),
            credentials: StubCredentials(.failure(CredentialError.notFound)),
            cache: SnapshotCache(fileURL: url),
            preferences: preferences,
            now: { Date(timeIntervalSince1970: 0) }
        )
        await store.refreshNow()
        XCTAssertEqual(store.state, .error(.noManualToken))
    }
}
