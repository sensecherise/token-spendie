import XCTest
@testable import TokenSpendie

final class ClaudeProviderTests: XCTestCase {

    // --- Test doubles ---
    final class StubCredentials: CredentialStore {
        var result: Result<OAuthCredentials, Error>
        var exists: Bool
        var loadCount = 0
        init(_ result: Result<OAuthCredentials, Error>, exists: Bool = true) {
            self.result = result
            self.exists = exists
        }
        func loadCredentials() throws -> OAuthCredentials {
            loadCount += 1
            return try result.get()
        }
        func credentialsExist() -> Bool { exists }
    }
    final class StubEndpoint: ClaudeUsageEndpoint {
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
    private func usageSnapshot() -> UsageSnapshot {
        UsageSnapshot(
            session: UsageWindow(percent: 47, resetsAt: Date(timeIntervalSince1970: 100)),
            weekly: UsageWindow(percent: 31, resetsAt: Date(timeIntervalSince1970: 200)),
            modelWeeklies: [ModelWeekly(model: "Opus",
                                        window: UsageWindow(percent: 62, resetsAt: nil))],
            fetchedAt: Date(timeIntervalSince1970: 999))
    }

    // --- detectCredentials ---

    func testDetectCredentialsReflectsKeychainExistence() {
        let present = ClaudeProvider(credentials: StubCredentials(.success(creds()), exists: true),
                                     endpoint: StubEndpoint([]))
        XCTAssertTrue(present.detectCredentials())
        let absent = ClaudeProvider(credentials: StubCredentials(.success(creds()), exists: false),
                                    endpoint: StubEndpoint([]))
        XCTAssertFalse(absent.detectCredentials())
    }

    // --- conversion ---

    func testConvertMapsWindowsWithLabelsAndHeadline() {
        let snapshot = ClaudeProvider.convert(usageSnapshot())
        XCTAssertEqual(snapshot.id, .claude)
        XCTAssertEqual(snapshot.headline.label, "Session · 5h")
        XCTAssertEqual(snapshot.headline.window.percent, 47, accuracy: 0.001)
        XCTAssertEqual(snapshot.windows.map(\.label),
                       ["Session · 5h", "Weekly · all", "Weekly · Opus"])
        XCTAssertEqual(snapshot.windows[0].resetStyle, .countdown)
        XCTAssertEqual(snapshot.windows[1].resetStyle, .date)
        XCTAssertEqual(snapshot.windows[1].detail, "all models")
        XCTAssertEqual(snapshot.windows[2].detail, "Opus only")
        XCTAssertEqual(snapshot.fetchedAt, Date(timeIntervalSince1970: 999))
        XCTAssertNil(snapshot.plan)
    }

    // --- fetchUsage ---

    func testFetchUsageReturnsConvertedSnapshot() async throws {
        let provider = ClaudeProvider(credentials: StubCredentials(.success(creds())),
                                      endpoint: StubEndpoint([.success(usageSnapshot())]))
        let snapshot = try await provider.fetchUsage()
        XCTAssertEqual(snapshot.headline.window.percent, 47, accuracy: 0.001)
    }

    func testFetchUsageRetriesOnceByRereadingKeychainOn401() async throws {
        let credentials = StubCredentials(.success(creds()))
        let endpoint = StubEndpoint([.failure(ProviderError.unauthorized),
                                     .success(usageSnapshot())])
        let provider = ClaudeProvider(credentials: credentials, endpoint: endpoint)
        _ = try await provider.fetchUsage()
        XCTAssertEqual(credentials.loadCount, 2, "re-reads the Keychain once on 401")
        XCTAssertEqual(endpoint.callCount, 2)
    }

    func testFetchUsagePropagatesPersistentUnauthorized() async {
        let provider = ClaudeProvider(
            credentials: StubCredentials(.success(creds())),
            endpoint: StubEndpoint([.failure(ProviderError.unauthorized),
                                    .failure(ProviderError.unauthorized)]))
        do {
            _ = try await provider.fetchUsage()
            XCTFail("expected unauthorized")
        } catch {
            XCTAssertEqual(error as? ProviderError, .unauthorized)
        }
    }

    func testFetchUsagePropagatesCredentialError() async {
        let provider = ClaudeProvider(
            credentials: StubCredentials(.failure(CredentialError.notFound)),
            endpoint: StubEndpoint([]))
        do {
            _ = try await provider.fetchUsage()
            XCTFail("expected notFound")
        } catch {
            XCTAssertEqual(error as? CredentialError, .notFound)
        }
    }
}
