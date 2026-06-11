import XCTest
@testable import TokenSpendie

final class CodexProviderTests: XCTestCase {
    private var dir: URL!

    override func setUpWithError() throws {
        dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("codex-provider-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: dir)
    }

    // MARK: - Fixtures

    private static let usageBody = Data(#"""
    {
      "plan_type": "pro",
      "rate_limit": {
        "primary_window":   {"used_percent": 15, "reset_at": 1735401600, "limit_window_seconds": 18000},
        "secondary_window": {"used_percent": 5,  "reset_at": 1735920000, "limit_window_seconds": 604800}
      },
      "credits": {"has_credits": true, "unlimited": false, "balance": 150.0}
    }
    """#.utf8)

    private static let secondaryOnlyBody = Data(#"""
    {"plan_type": "plus",
     "rate_limit": {"secondary_window": {"used_percent": 40, "reset_at": 1735920000, "limit_window_seconds": 604800}}}
    """#.utf8)

    private func freshStore(lastRefresh: String = "2026-06-10T00:00:00Z") throws -> CodexCredentialsStore {
        let url = dir.appendingPathComponent("auth.json")
        try Data(#"""
        {"tokens": {"access_token": "tok-1", "refresh_token": "ref-1", "account_id": "acct-1"},
         "last_refresh": "\#(lastRefresh)"}
        """#.utf8).write(to: url)
        return CodexCredentialsStore(fileURL: url)
    }

    private func http(_ status: Int, url: String = "https://chatgpt.com/backend-api/wham/usage") -> HTTPURLResponse {
        HTTPURLResponse(url: URL(string: url)!, statusCode: status,
                        httpVersion: nil, headerFields: [:])!
    }

    // The provider clock: one day after the fixture's last_refresh.
    private let now = { UsageDecoder.parseDate("2026-06-11T00:00:00Z")! }

    // MARK: - Decoding / mapping

    func testFetchMapsWindowsPlanAndHeadline() async throws {
        var captured: URLRequest?
        let provider = CodexProvider(
            store: try freshStore(),
            transport: { request in
                captured = request
                return (Self.usageBody, self.http(200))
            },
            now: now)
        let snapshot = try await provider.fetchUsage()

        XCTAssertEqual(snapshot.id, .codex)
        XCTAssertEqual(snapshot.plan, "Pro")
        XCTAssertEqual(snapshot.headline.label, "Session · 5h")
        XCTAssertEqual(snapshot.headline.window.percent, 15, accuracy: 0.001)
        XCTAssertEqual(snapshot.headline.resetStyle, .countdown)
        XCTAssertEqual(snapshot.windows.map(\.label), ["Session · 5h", "Weekly"])
        XCTAssertEqual(snapshot.windows[1].window.percent, 5, accuracy: 0.001)
        XCTAssertEqual(snapshot.windows[1].resetStyle, .date)
        XCTAssertEqual(snapshot.windows[1].window.resetsAt,
                       Date(timeIntervalSince1970: 1735920000))
        XCTAssertEqual(snapshot.fetchedAt, now())

        XCTAssertEqual(captured?.value(forHTTPHeaderField: "Authorization"), "Bearer tok-1")
        XCTAssertEqual(captured?.value(forHTTPHeaderField: "ChatGPT-Account-Id"), "acct-1")
    }

    func testMissingPrimaryFallsBackToSecondaryHeadline() async throws {
        let provider = CodexProvider(
            store: try freshStore(),
            transport: { _ in (Self.secondaryOnlyBody, self.http(200)) },
            now: now)
        let snapshot = try await provider.fetchUsage()
        XCTAssertEqual(snapshot.headline.label, "Weekly")
        XCTAssertEqual(snapshot.windows.count, 1)
        XCTAssertEqual(snapshot.plan, "Plus")
    }

    func testNoWindowsThrowsBadResponse() async throws {
        let provider = CodexProvider(
            store: try freshStore(),
            transport: { _ in (Data(#"{"plan_type": "pro"}"#.utf8), self.http(200)) },
            now: now)
        do {
            _ = try await provider.fetchUsage()
            XCTFail("expected badResponse")
        } catch {
            XCTAssertEqual(error as? ProviderError, .badResponse)
        }
    }

    // MARK: - Refresh paths

    func testStaleLastRefreshRefreshesBeforeUsageAndWritesBack() async throws {
        let store = try freshStore(lastRefresh: "2026-05-01T00:00:00Z") // 41 days stale
        var urls: [String] = []
        let provider = CodexProvider(
            store: store,
            transport: { request in
                let url = request.url!.absoluteString
                urls.append(url)
                if url.contains("auth.openai.com") {
                    let body = Data(#"{"access_token": "tok-2", "refresh_token": "ref-2"}"#.utf8)
                    return (body, self.http(200, url: url))
                }
                return (Self.usageBody, self.http(200))
            },
            now: now)
        _ = try await provider.fetchUsage()

        XCTAssertEqual(urls.first, "https://auth.openai.com/oauth/token")
        let saved = try store.load()
        XCTAssertEqual(saved.accessToken, "tok-2")
        XCTAssertEqual(saved.refreshToken, "ref-2")
        XCTAssertEqual(saved.lastRefresh, now())
    }

    func testUsage401RefreshesOnceAndRetries() async throws {
        let store = try freshStore() // fresh stamp — no proactive refresh
        var usageCalls = 0
        let provider = CodexProvider(
            store: store,
            transport: { request in
                let url = request.url!.absoluteString
                if url.contains("auth.openai.com") {
                    return (Data(#"{"access_token": "tok-2"}"#.utf8), self.http(200, url: url))
                }
                usageCalls += 1
                if usageCalls == 1 { return (Data(), self.http(401)) }
                return (Self.usageBody, self.http(200))
            },
            now: now)
        let snapshot = try await provider.fetchUsage()
        XCTAssertEqual(usageCalls, 2)
        XCTAssertEqual(snapshot.plan, "Pro")
        // Refresh response had no refresh_token → the old one must survive.
        XCTAssertEqual(try store.load().refreshToken, "ref-1")
    }

    func testSecond401ThrowsReauthRequired() async throws {
        let provider = CodexProvider(
            store: try freshStore(),
            transport: { request in
                let url = request.url!.absoluteString
                if url.contains("auth.openai.com") {
                    return (Data(#"{"access_token": "tok-2"}"#.utf8), self.http(200, url: url))
                }
                return (Data(), self.http(401))
            },
            now: now)
        do {
            _ = try await provider.fetchUsage()
            XCTFail("expected reauthRequired")
        } catch {
            XCTAssertEqual(error as? ProviderError, .reauthRequired)
        }
    }

    func testRefreshRejectionThrowsReauthRequired() async throws {
        let store = try freshStore(lastRefresh: "2026-05-01T00:00:00Z")
        let provider = CodexProvider(
            store: store,
            transport: { request in
                (Data(), self.http(401, url: request.url!.absoluteString))
            },
            now: now)
        do {
            _ = try await provider.fetchUsage()
            XCTFail("expected reauthRequired")
        } catch {
            XCTAssertEqual(error as? ProviderError, .reauthRequired)
        }
    }

    func testUsage429ThrowsRateLimited() async throws {
        let provider = CodexProvider(
            store: try freshStore(),
            transport: { _ in (Data(), self.http(429)) },
            now: now)
        do {
            _ = try await provider.fetchUsage()
            XCTFail("expected rateLimited")
        } catch {
            XCTAssertEqual(error as? ProviderError, .rateLimited(retryAfter: nil))
        }
    }

    func testDetectDelegatesToStore() throws {
        XCTAssertTrue(CodexProvider(store: try freshStore(),
                                    transport: DefaultTransport.shared).detectCredentials())
        let missing = CodexCredentialsStore(fileURL: dir.appendingPathComponent("nope.json"))
        XCTAssertFalse(CodexProvider(store: missing,
                                     transport: DefaultTransport.shared).detectCredentials())
    }

    func testWindowLabelsDeriveFromDuration() {
        // Non-5h primary and non-7d secondary get duration-derived labels.
        let body: [String: Any] = ["rate_limit": [
            "primary_window": ["used_percent": 1, "limit_window_seconds": 21600],
            "secondary_window": ["used_percent": 2, "limit_window_seconds": 14 * 86400],
        ]]
        let data = try! JSONSerialization.data(withJSONObject: body)
        let snapshot = try! CodexProvider.decode(data, fetchedAt: Date())
        XCTAssertEqual(snapshot.windows.map(\.label), ["Session · 6h", "14-day"])
    }
}
