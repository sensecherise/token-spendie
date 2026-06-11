import XCTest
@testable import TokenSpendie

final class CodexCredentialsStoreTests: XCTestCase {
    private var dir: URL!

    override func setUpWithError() throws {
        dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("codex-store-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: dir)
    }

    private func write(_ json: String) throws -> CodexCredentialsStore {
        let url = dir.appendingPathComponent("auth.json")
        try Data(json.utf8).write(to: url)
        return CodexCredentialsStore(fileURL: url)
    }

    private static let fullAuthJSON = #"""
    {
      "OPENAI_API_KEY": null,
      "tokens": {
        "id_token": "id.jwt",
        "access_token": "access.jwt",
        "refresh_token": "refresh-1",
        "account_id": "account-123"
      },
      "last_refresh": "2026-06-01T00:00:00Z",
      "future_field": {"keep": true}
    }
    """#

    func testLoadParsesTokens() throws {
        let store = try write(Self.fullAuthJSON)
        let creds = try store.load()
        XCTAssertEqual(creds.accessToken, "access.jwt")
        XCTAssertEqual(creds.refreshToken, "refresh-1")
        XCTAssertEqual(creds.accountId, "account-123")
        XCTAssertEqual(creds.lastRefresh,
                       UsageDecoder.parseDate("2026-06-01T00:00:00Z"))
    }

    func testDetectTrueOnlyWithNonEmptyAccessToken() throws {
        XCTAssertTrue(try write(Self.fullAuthJSON).detect())
        // API-key-only file → not detected (spec: same policy as Gemini API-key).
        XCTAssertFalse(try write(#"{"OPENAI_API_KEY": "sk-x"}"#).detect())
        XCTAssertFalse(try write(#"{"tokens": {"access_token": ""}}"#).detect())
        let missing = CodexCredentialsStore(
            fileURL: dir.appendingPathComponent("nope.json"))
        XCTAssertFalse(missing.detect())
    }

    func testLoadMissingOrMalformedThrowsReauthRequired() throws {
        let missing = CodexCredentialsStore(
            fileURL: dir.appendingPathComponent("nope.json"))
        XCTAssertThrowsError(try missing.load()) {
            XCTAssertEqual($0 as? ProviderError, .reauthRequired)
        }
        let malformed = try write("not json")
        XCTAssertThrowsError(try malformed.load()) {
            XCTAssertEqual($0 as? ProviderError, .reauthRequired)
        }
    }

    func testNeedsRefreshAfterEightDays() throws {
        let creds = try write(Self.fullAuthJSON).load()
        let lastRefresh = creds.lastRefresh!
        XCTAssertFalse(creds.needsRefresh(now: lastRefresh.addingTimeInterval(7 * 86400)))
        XCTAssertTrue(creds.needsRefresh(now: lastRefresh.addingTimeInterval(9 * 86400)))
        // Exactly 8 days is NOT "older than 8 days" — strict >.
        XCTAssertFalse(creds.needsRefresh(now: lastRefresh.addingTimeInterval(8 * 86400)))
        let noStamp = CodexCredentials(accessToken: "a", refreshToken: "r",
                                       idToken: nil, accountId: nil, lastRefresh: nil)
        XCTAssertTrue(noStamp.needsRefresh(now: Date()))
    }

    func testSavePreservesUnknownFieldsAndUpdatesTokens() throws {
        let store = try write(Self.fullAuthJSON)
        let updated = CodexCredentials(accessToken: "access-2", refreshToken: "refresh-2",
                                       idToken: "id-2", accountId: "account-123",
                                       lastRefresh: nil)
        try store.save(updated, refreshedAt: Date(timeIntervalSince1970: 1_780_000_000))

        let reloaded = try store.load()
        XCTAssertEqual(reloaded.accessToken, "access-2")
        XCTAssertEqual(reloaded.refreshToken, "refresh-2")
        XCTAssertEqual(reloaded.idToken, "id-2")
        XCTAssertNotNil(reloaded.lastRefresh)

        let raw = try JSONSerialization.jsonObject(
            with: Data(contentsOf: store.fileURL)) as! [String: Any]
        XCTAssertNotNil(raw["future_field"], "unknown top-level fields must survive")
        XCTAssertTrue(raw["OPENAI_API_KEY"] is NSNull, "null API key must survive")
    }

    func testSaveOnMissingFileCreatesOwnerOnlyPermissions() throws {
        let store = CodexCredentialsStore(fileURL: dir.appendingPathComponent("auth.json"))
        let creds = CodexCredentials(accessToken: "a", refreshToken: "r",
                                     idToken: nil, accountId: nil, lastRefresh: nil)
        try store.save(creds, refreshedAt: Date())
        let attrs = try FileManager.default.attributesOfItem(atPath: store.fileURL.path)
        XCTAssertEqual((attrs[.posixPermissions] as? NSNumber)?.int16Value, 0o600)
    }

    func testDefaultURLHonorsCodexHome() {
        let url = CodexCredentialsStore.defaultURL(
            environment: ["CODEX_HOME": "/tmp/custom-codex"],
            home: URL(fileURLWithPath: "/Users/x"))
        XCTAssertEqual(url.path, "/tmp/custom-codex/auth.json")
        let fallback = CodexCredentialsStore.defaultURL(
            environment: [:], home: URL(fileURLWithPath: "/Users/x"))
        XCTAssertEqual(fallback.path, "/Users/x/.codex/auth.json")
    }
}
