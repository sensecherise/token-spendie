# Codex Provider Implementation Plan (Phase 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an OpenAI Codex usage row (Session 5h + Weekly windows + plan pill) to Token Spendie on macOS and Windows, reading `~/.codex/auth.json` and calling the `wham/usage` endpoint, with 8-day token refresh + atomic write-back.

**Architecture:** A new `CodexProvider` conforms to the existing `UsageProvider` protocol (Swift) / `IUsageProvider` interface (C#). It composes three focused units: a credentials store (auth.json read/write), a token refresher (OAuth refresh grant), and a usage endpoint (the `wham/usage` GET). New `ProviderError.reauthRequired` → `UsageError.codexReauthRequired` carries the "run codex to re-auth" state to the panel. Spec: `docs/superpowers/specs/2026-06-12-codex-antigravity-providers-design.md`.

**Tech Stack:** Swift 5 / XCTest (injected `HTTPTransport` closures, no network in tests); C# .NET 8 / xUnit + FluentAssertions + NSubstitute. macOS tests: `swift test` (needs full Xcode). Windows tests: `dotnet test` (Windows machine or `windows-ci.yml` CI).

**Branch:** `feature/codex-provider` off `develop`. The TEMP-debug working-tree changes (credential-cache debug hooks in AppDelegate/KeychainReader/MenuBar/FloatingPanel/DetailPanel/EndpointUsageProvider) must NOT be committed — `git add` only the files each task names.

**Reference protocol facts** (verified 2026-06-12 against CodexBar, MIT):

- `auth.json`: `{ "OPENAI_API_KEY": …|null, "tokens": { "id_token", "access_token", "refresh_token", "account_id" }, "last_refresh": "2025-12-28T12:34:56Z" }`
- Usage: `GET https://chatgpt.com/backend-api/wham/usage`, headers `Authorization: Bearer <access_token>`, `ChatGPT-Account-Id: <account_id>` (when present), `Accept: application/json`, `User-Agent: TokenSpendie/1.0`. Response:

```json
{
  "plan_type": "pro",
  "rate_limit": {
    "primary_window":   { "used_percent": 15, "reset_at": 1735401600, "limit_window_seconds": 18000 },
    "secondary_window": { "used_percent": 5,  "reset_at": 1735920000, "limit_window_seconds": 604800 }
  },
  "credits": { "has_credits": true, "unlimited": false, "balance": 150.0 }
}
```

  Either window may be absent. `credits` is ignored.
- Refresh: `POST https://auth.openai.com/oauth/token`, JSON body `{ "client_id": "app_EMoamEEZ73f0CkXaXp7hrann", "grant_type": "refresh_token", "refresh_token": "<refresh_token>", "scope": "openid profile email" }`. 200 → `{ "id_token", "access_token", "refresh_token" }` (any may be absent → keep old value). Refresh when `last_refresh` older than 8 days, or after a usage 401.

---

## File structure

**macOS (Swift):**

| File | Responsibility |
|---|---|
| `Sources/TokenSpendie/Model/UsageModels.swift` (modify) | `ProviderID.codex`, `ProviderError.reauthRequired`, `UsageError.codexReauthRequired` |
| `Sources/TokenSpendie/Data/CodexCredentialsStore.swift` (create) | auth.json model + load/detect/atomic write-back |
| `Sources/TokenSpendie/Data/CodexProvider.swift` (create) | refresher + endpoint + `UsageProvider` conformance + snapshot mapping |
| `Sources/TokenSpendie/Store/UsageStore.swift` (modify) | map `reauthRequired` |
| `Sources/TokenSpendie/UI/DetailPanelView.swift` (modify) | panel copy for `codexReauthRequired` |
| `Sources/TokenSpendie/AppDelegate.swift` (modify) | register provider |
| `Tests/TokenSpendieTests/CodexCredentialsStoreTests.swift`, `Tests/TokenSpendieTests/CodexProviderTests.swift` (create) | unit tests |

**Windows (C#):** mirrors — `Models/ProviderID.cs`, `Models/Errors.cs`, new `Data/CodexCredentialsStore.cs`, new `Data/CodexProvider.cs` (+ `ICodexUsageEndpoint`/`ICodexTokenRefresher` interfaces), `Services/UsageStore.cs` catch arm, `App.xaml.cs` registration, `TokenSpendie.WidgetProvider/Data/SnapshotFetcher.cs` fallthrough, tests under `windows/tests/TokenSpendie.Windows.Tests/Data/`.

---

### Task 0: Branch

- [ ] **Step 0.1: Create the feature branch**

```bash
git checkout develop && git checkout -b feature/codex-provider
git status --short   # TEMP-debug modifications may be present; never `git add` them
```

---

### Task 1: Model cases (Swift)

**Files:**
- Modify: `Sources/TokenSpendie/Model/UsageModels.swift`
- Modify: `Sources/TokenSpendie/UI/DetailPanelView.swift` (compiler will force the new `panelMessage` case)
- Test: `Tests/TokenSpendieTests/ProviderModelsTests.swift` (add one test)

- [ ] **Step 1.1: Write the failing test**

Append to `Tests/TokenSpendieTests/ProviderModelsTests.swift`:

```swift
func testCodexProviderIDRawValueRoundTrips() throws {
    XCTAssertEqual(ProviderID.codex.rawValue, "codex")
    let decoded = try JSONDecoder().decode(ProviderID.self,
                                           from: Data(#""codex""#.utf8))
    XCTAssertEqual(decoded, .codex)
}
```

- [ ] **Step 1.2: Run it — expect FAIL**

Run: `swift test --filter ProviderModelsTests 2>&1 | tail -5`
Expected: compile error `type 'ProviderID' has no member 'codex'`.

- [ ] **Step 1.3: Add the enum cases**

In `Sources/TokenSpendie/Model/UsageModels.swift`:

```swift
enum ProviderID: String, Codable, CaseIterable, Equatable {
    case claude
    case gemini
    case codex
}
```

In `UsageError` (same file):

```swift
enum UsageError: Error, Equatable {
    case claudeCodeNotFound     // no Keychain item / Claude Code not logged in
    case keychainAccessDenied   // user denied the Keychain access prompt
    case loginExpired           // 401 even after re-reading the Keychain
    case codexReauthRequired    // Codex 401/refresh failure — user must run `codex`
    case network                // offline / unreachable
    case badResponse            // non-200 or unparseable payload
}
```

In `ProviderError` (same file):

```swift
enum ProviderError: Error, Equatable {
    case unauthorized           // HTTP 401
    case reauthRequired         // credentials unusable and unrefreshable — re-login needed
    case network                // transport failure
    case badResponse            // non-200, or payload could not be decoded
    case rateLimited(retryAfter: TimeInterval?)  // HTTP 429
}
```

- [ ] **Step 1.4: Fix the exhaustive `panelMessage` switch**

In `Sources/TokenSpendie/UI/DetailPanelView.swift`, the `panelMessage(for:)` switch gains (after the `.loginExpired` case):

```swift
case .codexReauthRequired:
    return ("⏱", "Codex login needed. Run `codex` in a terminal to sign in again — the widget recovers automatically.")
```

- [ ] **Step 1.5: Map the new error in the store**

In `Sources/TokenSpendie/Store/UsageStore.swift`, inside `refresh(_:ignoringBackoff:)`, add after the `catch ProviderError.unauthorized` arm:

```swift
} catch ProviderError.reauthRequired {
    setState(.error(.codexReauthRequired), for: id)
```

- [ ] **Step 1.6: Run the full suite — expect PASS**

Run: `swift test 2>&1 | tail -3`
Expected: `Test Suite 'All tests' passed`.

- [ ] **Step 1.7: Commit**

```bash
git add Sources/TokenSpendie/Model/UsageModels.swift Sources/TokenSpendie/Store/UsageStore.swift Sources/TokenSpendie/UI/DetailPanelView.swift Tests/TokenSpendieTests/ProviderModelsTests.swift
git commit -m "feat(codex): add codex ProviderID and reauth error cases"
```

---

### Task 2: CodexCredentialsStore (Swift)

**Files:**
- Create: `Sources/TokenSpendie/Data/CodexCredentialsStore.swift`
- Test: `Tests/TokenSpendieTests/CodexCredentialsStoreTests.swift`

- [ ] **Step 2.1: Write the failing tests**

Create `Tests/TokenSpendieTests/CodexCredentialsStoreTests.swift`:

```swift
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
```

- [ ] **Step 2.2: Run — expect FAIL**

Run: `swift test --filter CodexCredentialsStoreTests 2>&1 | tail -5`
Expected: compile error `cannot find 'CodexCredentialsStore' in scope`.

- [ ] **Step 2.3: Implement the store**

Create `Sources/TokenSpendie/Data/CodexCredentialsStore.swift`:

```swift
import Foundation

/// Codex CLI's OAuth token set, as stored in `auth.json`.
struct CodexCredentials: Equatable {
    let accessToken: String
    let refreshToken: String
    let idToken: String?
    let accountId: String?
    let lastRefresh: Date?

    /// Codex CLI's own policy: refresh when `last_refresh` is older than
    /// 8 days (or missing).
    func needsRefresh(now: Date) -> Bool {
        guard let lastRefresh else { return true }
        return now.timeIntervalSince(lastRefresh) > 8 * 86400
    }
}

/// Reads and (after a token refresh) rewrites Codex CLI's `auth.json`.
/// Write-back preserves every field this app does not own — the file belongs
/// to Codex CLI; we only rotate the token set, exactly like the CLI itself.
struct CodexCredentialsStore {
    let fileURL: URL

    /// `$CODEX_HOME/auth.json` when set, else `~/.codex/auth.json`.
    static func defaultURL(
        environment: [String: String] = ProcessInfo.processInfo.environment,
        home: URL = FileManager.default.homeDirectoryForCurrentUser) -> URL {
        if let codexHome = environment["CODEX_HOME"], !codexHome.isEmpty {
            return URL(fileURLWithPath: codexHome).appendingPathComponent("auth.json")
        }
        return home.appendingPathComponent(".codex", isDirectory: true)
            .appendingPathComponent("auth.json")
    }

    /// True when `auth.json` holds a non-empty OAuth access token. API-key-only
    /// files are not detected — the usage endpoint needs the ChatGPT-plan OAuth
    /// token. Cheap (one small file read), never prompts.
    func detect() -> Bool {
        guard let creds = try? load() else { return false }
        return !creds.accessToken.isEmpty
    }

    /// Loads the token set. Any missing/malformed state maps to
    /// `ProviderError.reauthRequired` — by the time `load()` runs the provider
    /// was detected, so a broken file means "log in again".
    func load() throws -> CodexCredentials {
        guard let data = try? Data(contentsOf: fileURL),
              let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let tokens = root["tokens"] as? [String: Any],
              let accessToken = tokens["access_token"] as? String,
              let refreshToken = tokens["refresh_token"] as? String else {
            throw ProviderError.reauthRequired
        }
        let lastRefresh = (root["last_refresh"] as? String)
            .flatMap(UsageDecoder.parseDate)
        return CodexCredentials(accessToken: accessToken,
                                refreshToken: refreshToken,
                                idToken: tokens["id_token"] as? String,
                                accountId: tokens["account_id"] as? String,
                                lastRefresh: lastRefresh)
    }

    /// Atomically rewrites `auth.json` with the refreshed token set, keeping
    /// all fields we do not own (`OPENAI_API_KEY`, unknown future fields).
    func save(_ creds: CodexCredentials, refreshedAt: Date) throws {
        var root: [String: Any] = [:]
        if let data = try? Data(contentsOf: fileURL),
           let existing = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] {
            root = existing
        }
        var tokens = (root["tokens"] as? [String: Any]) ?? [:]
        tokens["access_token"] = creds.accessToken
        tokens["refresh_token"] = creds.refreshToken
        if let idToken = creds.idToken { tokens["id_token"] = idToken }
        if let accountId = creds.accountId { tokens["account_id"] = accountId }
        root["tokens"] = tokens
        root["last_refresh"] = Self.isoFormatter.string(from: refreshedAt)

        let data = try JSONSerialization.data(
            withJSONObject: root, options: [.prettyPrinted, .sortedKeys])
        try data.write(to: fileURL, options: .atomic)
    }

    private static let isoFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()
}
```

- [ ] **Step 2.4: Run — expect PASS**

Run: `swift test --filter CodexCredentialsStoreTests 2>&1 | tail -3`
Expected: all CodexCredentialsStoreTests pass.

- [ ] **Step 2.5: Commit**

```bash
git add Sources/TokenSpendie/Data/CodexCredentialsStore.swift Tests/TokenSpendieTests/CodexCredentialsStoreTests.swift
git commit -m "feat(codex): auth.json credentials store with preserving write-back"
```

---

### Task 3: CodexProvider — refresher, endpoint, mapping (Swift)

**Files:**
- Create: `Sources/TokenSpendie/Data/CodexProvider.swift`
- Test: `Tests/TokenSpendieTests/CodexProviderTests.swift`

- [ ] **Step 3.1: Write the failing tests**

Create `Tests/TokenSpendieTests/CodexProviderTests.swift`:

```swift
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

    // The provider clock: 2026-06-12, one day after the fixture's last_refresh.
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
```

- [ ] **Step 3.2: Run — expect FAIL**

Run: `swift test --filter CodexProviderTests 2>&1 | tail -5`
Expected: compile error `cannot find 'CodexProvider' in scope`.

- [ ] **Step 3.3: Implement the provider**

Create `Sources/TokenSpendie/Data/CodexProvider.swift`:

```swift
import Foundation

/// The `UsageProvider` for OpenAI Codex. Reads the CLI's OAuth tokens from
/// `auth.json`, refreshes them when stale (Codex CLI's own 8-day policy) or on
/// a 401, writes refreshed tokens back (refresh tokens rotate — discarding the
/// new one would invalidate the CLI's login), and calls the `wham/usage`
/// endpoint Codex itself uses.
struct CodexProvider: UsageProvider {
    let id: ProviderID = .codex
    let displayName: String = "Codex"

    private static let usageURL = URL(string: "https://chatgpt.com/backend-api/wham/usage")!
    private static let refreshURL = URL(string: "https://auth.openai.com/oauth/token")!
    /// Codex CLI's public OAuth client id (from codex-rs source).
    private static let clientID = "app_EMoamEEZ73f0CkXaXp7hrann"

    private let store: CodexCredentialsStore
    private let transport: HTTPTransport
    private let now: () -> Date

    init(store: CodexCredentialsStore = CodexCredentialsStore(fileURL: CodexCredentialsStore.defaultURL()),
         transport: @escaping HTTPTransport = DefaultTransport.shared,
         now: @escaping () -> Date = Date.init) {
        self.store = store
        self.transport = transport
        self.now = now
    }

    func detectCredentials() -> Bool {
        store.detect()
    }

    func fetchUsage() async throws -> ProviderSnapshot {
        var creds = try store.load()
        if creds.needsRefresh(now: now()) {
            creds = try await refreshAndSave(creds)
        }
        do {
            return try await fetchSnapshot(with: creds)
        } catch ProviderError.unauthorized {
            creds = try await refreshAndSave(creds)
            do {
                return try await fetchSnapshot(with: creds)
            } catch ProviderError.unauthorized {
                throw ProviderError.reauthRequired
            }
        }
    }

    // MARK: - Usage endpoint

    private func fetchSnapshot(with creds: CodexCredentials) async throws -> ProviderSnapshot {
        var request = URLRequest(url: Self.usageURL)
        request.httpMethod = "GET"
        request.setValue("Bearer \(creds.accessToken)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("TokenSpendie/1.0", forHTTPHeaderField: "User-Agent")
        if let accountId = creds.accountId {
            request.setValue(accountId, forHTTPHeaderField: "ChatGPT-Account-Id")
        }

        let (data, response) = try await transport(request)
        switch response.statusCode {
        case 200:
            return try Self.decode(data, fetchedAt: now())
        case 401, 403:
            throw ProviderError.unauthorized
        case 429:
            let retryAfter = response.value(forHTTPHeaderField: "Retry-After")
                .flatMap(TimeInterval.init)
            throw ProviderError.rateLimited(retryAfter: retryAfter)
        default:
            throw ProviderError.badResponse
        }
    }

    // MARK: - Token refresh

    /// One refresh round-trip + write-back. Any rejection means the refresh
    /// token is dead → `reauthRequired`.
    private func refreshAndSave(_ creds: CodexCredentials) async throws -> CodexCredentials {
        var request = URLRequest(url: Self.refreshURL)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: [
            "client_id": Self.clientID,
            "grant_type": "refresh_token",
            "refresh_token": creds.refreshToken,
            "scope": "openid profile email",
        ])

        let (data, response) = try await transport(request)
        guard response.statusCode == 200,
              let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            // 400/401/anything-not-200: the stored refresh token is unusable.
            throw ProviderError.reauthRequired
        }
        // Fields absent from the response keep their stored values.
        let refreshed = CodexCredentials(
            accessToken: json["access_token"] as? String ?? creds.accessToken,
            refreshToken: json["refresh_token"] as? String ?? creds.refreshToken,
            idToken: json["id_token"] as? String ?? creds.idToken,
            accountId: creds.accountId,
            lastRefresh: now())
        try store.save(refreshed, refreshedAt: now())
        return refreshed
    }

    // MARK: - Decoding / mapping

    /// Decodes the `wham/usage` payload straight into a `ProviderSnapshot`.
    /// `primary_window` (≈5 h) is the headline; either window may be absent;
    /// neither present → `badResponse`. `credits` is ignored.
    static func decode(_ data: Data, fetchedAt: Date) throws -> ProviderSnapshot {
        guard let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            throw ProviderError.badResponse
        }
        let rateLimit = root["rate_limit"] as? [String: Any] ?? [:]

        func window(_ key: String) -> (window: UsageWindow, seconds: Int?)? {
            guard let raw = rateLimit[key] as? [String: Any],
                  let used = (raw["used_percent"] as? NSNumber)?.doubleValue else {
                return nil
            }
            let resetsAt = (raw["reset_at"] as? NSNumber)
                .map { Date(timeIntervalSince1970: $0.doubleValue) }
            let seconds = (raw["limit_window_seconds"] as? NSNumber)?.intValue
            return (UsageWindow(percent: used, resetsAt: resetsAt), seconds)
        }

        var windows: [LabeledWindow] = []
        if let primary = window("primary_window") {
            let hours = max(1, Int(((primary.seconds ?? 18000) + 1800) / 3600))
            windows.append(LabeledWindow(label: "Session · \(hours)h",
                                         detail: "\(hours)-hour window",
                                         resetStyle: .countdown,
                                         window: primary.window))
        }
        if let secondary = window("secondary_window") {
            let days = max(1, Int(((secondary.seconds ?? 604800) + 43200) / 86400))
            windows.append(LabeledWindow(label: days == 7 ? "Weekly" : "\(days)-day",
                                         detail: "\(days)-day window",
                                         resetStyle: .date,
                                         window: secondary.window))
        }
        guard let headline = windows.first else { throw ProviderError.badResponse }

        let plan = (root["plan_type"] as? String).map { raw in
            raw.split(separator: "_").map(\.capitalized).joined(separator: " ")
        }
        return ProviderSnapshot(id: .codex, plan: plan, headline: headline,
                                windows: windows, fetchedAt: fetchedAt)
    }
}
```

- [ ] **Step 3.4: Run — expect PASS**

Run: `swift test --filter CodexProviderTests 2>&1 | tail -3`
Expected: all CodexProviderTests pass.

- [ ] **Step 3.5: Run the full suite — expect PASS**

Run: `swift test 2>&1 | tail -3`

- [ ] **Step 3.6: Commit**

```bash
git add Sources/TokenSpendie/Data/CodexProvider.swift Tests/TokenSpendieTests/CodexProviderTests.swift
git commit -m "feat(codex): provider with usage fetch, 8-day refresh and 401 retry"
```

---

### Task 4: Register + manual smoke (macOS)

**Files:**
- Modify: `Sources/TokenSpendie/AppDelegate.swift:20`

- [ ] **Step 4.1: Register the provider**

In `Sources/TokenSpendie/AppDelegate.swift`, change the providers array (keep any TEMP-debug code around it untouched):

```swift
providers: [ClaudeProvider(), GeminiProvider(), CodexProvider()],
```

- [ ] **Step 4.2: Build and smoke-test**

Run: `./build.sh && open build/TokenSpendie.app`
Expected: app launches; with no `~/.codex`, NO Codex row appears (detection gate). If logged into Codex (`codex login` first), a CODEX section appears with Session + Weekly bars and a plan pill.

- [ ] **Step 4.3: Run the full suite once more, then commit**

Run: `swift test 2>&1 | tail -3`

```bash
git add Sources/TokenSpendie/AppDelegate.swift
git commit -m "feat(codex): register CodexProvider on macOS"
```

---

### Task 5: Model cases (Windows)

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/Models/ProviderID.cs`
- Modify: `windows/src/TokenSpendie.Windows/Models/Errors.cs`
- Modify: `windows/src/TokenSpendie.Windows/Services/UsageStore.cs` (catch arm)
- Test: `windows/tests/TokenSpendie.Windows.Tests/Models/ProviderIDTests.cs` (create if absent)

> Windows tasks need a Windows machine or the `windows-ci.yml` CI run. All
> `dotnet` commands below run from the `windows/` directory.

- [ ] **Step 5.1: Write the failing test**

Create (or append to) `windows/tests/TokenSpendie.Windows.Tests/Models/ProviderIDTests.cs`:

```csharp
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ProviderIDTests
{
    [Fact]
    public void CodexCaseExists()
    {
        System.Enum.GetNames<ProviderID>().Should().Contain("Codex");
        UsageErrorKind.CodexReauthRequired.Should().BeDefined();
    }
}
```

- [ ] **Step 5.2: Run — expect FAIL**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter ProviderIDTests 2>&1 | tail -5`
Expected: compile error `'ProviderID' does not contain a definition for 'Codex'`.

- [ ] **Step 5.3: Add the enum members and exception**

`windows/src/TokenSpendie.Windows/Models/ProviderID.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public enum ProviderID
{
    Claude,
    Gemini,
    Codex,
}
```

`windows/src/TokenSpendie.Windows/Models/Errors.cs` — extend the provider-error section:

```csharp
public enum ProviderErrorKind { Unauthorized, ReauthRequired, Network, BadResponse, RateLimited }
```

add alongside the other `ProviderException` subclasses:

```csharp
public sealed class ProviderReauthRequiredException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.ReauthRequired;
    public ProviderReauthRequiredException(string cli)
        : base($"{cli} re-authentication required.") { }
}
```

and extend the user-facing enum:

```csharp
public enum UsageErrorKind
{
    ClaudeCodeNotFound,
    CredentialAccessDenied,
    LoginExpired,
    CodexReauthRequired,
    Network,
    BadResponse,
}
```

- [ ] **Step 5.4: Map it in the store**

In `windows/src/TokenSpendie.Windows/Services/UsageStore.cs`, add a catch arm directly after the `catch (ProviderUnauthorizedException)` arm (around line 183):

```csharp
catch (ProviderReauthRequiredException)
{
    SetState(LoadState.Error(UsageErrorKind.CodexReauthRequired), id);
}
```

- [ ] **Step 5.5: Run — expect PASS, then commit**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter ProviderIDTests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/Models/ProviderID.cs windows/src/TokenSpendie.Windows/Models/Errors.cs windows/src/TokenSpendie.Windows/Services/UsageStore.cs windows/tests/TokenSpendie.Windows.Tests/Models/ProviderIDTests.cs
git commit -m "feat(codex,windows): ProviderID.Codex and reauth error plumbing"
```

---

### Task 6: CodexCredentialsStore (C#)

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/CodexCredentialsStore.cs`
- Test: `windows/tests/TokenSpendie.Windows.Tests/Data/CodexCredentialsStoreTests.cs`

- [ ] **Step 6.1: Write the failing tests**

Create `windows/tests/TokenSpendie.Windows.Tests/Data/CodexCredentialsStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public sealed class CodexCredentialsStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "codex-store-" + Guid.NewGuid().ToString("N"));

    public CodexCredentialsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string FullAuthJson = """
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
    """;

    private CodexCredentialsStore Write(string json)
    {
        var path = Path.Combine(_dir, "auth.json");
        File.WriteAllText(path, json);
        return new CodexCredentialsStore(path);
    }

    [Fact]
    public void LoadParsesTokens()
    {
        var creds = Write(FullAuthJson).Load();
        creds.AccessToken.Should().Be("access.jwt");
        creds.RefreshToken.Should().Be("refresh-1");
        creds.AccountId.Should().Be("account-123");
        creds.LastRefresh.Should().Be(DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
    }

    [Fact]
    public void DetectTrueOnlyWithNonEmptyAccessToken()
    {
        Write(FullAuthJson).Detect().Should().BeTrue();
        Write("""{"OPENAI_API_KEY": "sk-x"}""").Detect().Should().BeFalse();
        Write("""{"tokens": {"access_token": ""}}""").Detect().Should().BeFalse();
        new CodexCredentialsStore(Path.Combine(_dir, "nope.json")).Detect().Should().BeFalse();
    }

    [Fact]
    public void LoadMissingOrMalformedThrowsReauthRequired()
    {
        var missing = new CodexCredentialsStore(Path.Combine(_dir, "nope.json"));
        missing.Invoking(s => s.Load()).Should().Throw<ProviderReauthRequiredException>();
        Write("not json").Invoking(s => s.Load()).Should().Throw<ProviderReauthRequiredException>();
    }

    [Fact]
    public void NeedsRefreshAfterEightDays()
    {
        var creds = Write(FullAuthJson).Load();
        var stamp = creds.LastRefresh!.Value;
        creds.NeedsRefresh(stamp.AddDays(7)).Should().BeFalse();
        creds.NeedsRefresh(stamp.AddDays(9)).Should().BeTrue();
        (creds with { LastRefresh = null }).NeedsRefresh(DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void SavePreservesUnknownFieldsAndUpdatesTokens()
    {
        var store = Write(FullAuthJson);
        var updated = new CodexCredentials("access-2", "refresh-2", "id-2", "account-123", null);
        store.Save(updated, DateTimeOffset.FromUnixTimeSeconds(1_780_000_000));

        var reloaded = store.Load();
        reloaded.AccessToken.Should().Be("access-2");
        reloaded.RefreshToken.Should().Be("refresh-2");
        reloaded.LastRefresh.Should().NotBeNull();

        using var doc = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        doc.RootElement.TryGetProperty("future_field", out _)
            .Should().BeTrue("unknown top-level fields must survive");
        doc.RootElement.GetProperty("OPENAI_API_KEY").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void DefaultPathHonorsCodexHome()
    {
        CodexCredentialsStore.DefaultPath(codexHome: @"C:\custom", userProfile: @"C:\Users\x")
            .Should().Be(Path.Combine(@"C:\custom", "auth.json"));
        CodexCredentialsStore.DefaultPath(codexHome: null, userProfile: @"C:\Users\x")
            .Should().Be(Path.Combine(@"C:\Users\x", ".codex", "auth.json"));
    }
}
```

- [ ] **Step 6.2: Run — expect FAIL**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter CodexCredentialsStoreTests 2>&1 | tail -5`
Expected: compile error `The type or namespace name 'CodexCredentialsStore' could not be found`.

- [ ] **Step 6.3: Implement**

Create `windows/src/TokenSpendie.Windows/Data/CodexCredentialsStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

/// <summary>Codex CLI's OAuth token set, as stored in <c>auth.json</c>.</summary>
public sealed record CodexCredentials(
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    string? AccountId,
    DateTimeOffset? LastRefresh)
{
    /// <summary>Codex CLI's own policy: refresh when <c>last_refresh</c> is
    /// older than 8 days (or missing).</summary>
    public bool NeedsRefresh(DateTimeOffset now) =>
        LastRefresh is not { } stamp || (now - stamp) > TimeSpan.FromDays(8);
}

/// <summary>
/// Reads and (after a token refresh) rewrites Codex CLI's <c>auth.json</c>
/// (<c>%CODEX_HOME%\auth.json</c> or <c>%USERPROFILE%\.codex\auth.json</c>).
/// Write-back preserves every field this app does not own — the file belongs
/// to Codex CLI; we only rotate the token set, exactly like the CLI itself.
/// </summary>
public sealed class CodexCredentialsStore
{
    public string FilePath { get; }

    public CodexCredentialsStore(string path) => FilePath = path;

    public CodexCredentialsStore()
        : this(DefaultPath(
            Environment.GetEnvironmentVariable("CODEX_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))) { }

    public static string DefaultPath(string? codexHome, string userProfile) =>
        string.IsNullOrEmpty(codexHome)
            ? Path.Combine(userProfile, ".codex", "auth.json")
            : Path.Combine(codexHome, "auth.json");

    /// <summary>True when <c>auth.json</c> holds a non-empty OAuth access
    /// token. API-key-only files are not detected. Cheap, never prompts.</summary>
    public bool Detect()
    {
        try { return !string.IsNullOrEmpty(Load().AccessToken); }
        catch { return false; }
    }

    /// <summary>Loads the token set. Missing/malformed state maps to
    /// <see cref="ProviderReauthRequiredException"/> — by the time this runs
    /// the provider was detected, so a broken file means "log in again".</summary>
    public CodexCredentials Load()
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath));
        }
        catch (Exception)
        {
            throw new ProviderReauthRequiredException("Codex");
        }
        var tokens = root?["tokens"];
        var accessToken = tokens?["access_token"]?.GetValue<string>();
        var refreshToken = tokens?["refresh_token"]?.GetValue<string>();
        if (accessToken is null || refreshToken is null)
            throw new ProviderReauthRequiredException("Codex");

        DateTimeOffset? lastRefresh = null;
        if (root!["last_refresh"]?.GetValue<string>() is { } stamp &&
            DateTimeOffset.TryParse(stamp, out var parsed))
        {
            lastRefresh = parsed;
        }
        return new CodexCredentials(
            accessToken, refreshToken,
            tokens?["id_token"]?.GetValue<string>(),
            tokens?["account_id"]?.GetValue<string>(),
            lastRefresh);
    }

    /// <summary>Atomically rewrites <c>auth.json</c> with the refreshed token
    /// set, keeping all fields we do not own.</summary>
    public void Save(CodexCredentials creds, DateTimeOffset refreshedAt)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception)
        {
            root = new JsonObject();
        }
        var tokens = root["tokens"] as JsonObject ?? new JsonObject();
        tokens["access_token"] = creds.AccessToken;
        tokens["refresh_token"] = creds.RefreshToken;
        if (creds.IdToken is { } idToken) tokens["id_token"] = idToken;
        if (creds.AccountId is { } accountId) tokens["account_id"] = accountId;
        root["tokens"] = tokens;
        root["last_refresh"] = refreshedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
    }
}
```

- [ ] **Step 6.4: Run — expect PASS, then commit**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter CodexCredentialsStoreTests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/Data/CodexCredentialsStore.cs windows/tests/TokenSpendie.Windows.Tests/Data/CodexCredentialsStoreTests.cs
git commit -m "feat(codex,windows): auth.json credentials store with preserving write-back"
```

---

### Task 7: CodexProvider (C#)

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/CodexProvider.cs`
- Test: `windows/tests/TokenSpendie.Windows.Tests/Data/CodexProviderTests.cs`

The C# provider takes the same shape as the Swift one but, following the
existing `ClaudeProvider`/`IClaudeUsageEndpoint` pattern, splits the two HTTP
calls behind interfaces so tests use NSubstitute instead of a transport
closure.

- [ ] **Step 7.1: Write the failing tests**

Create `windows/tests/TokenSpendie.Windows.Tests/Data/CodexProviderTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public sealed class CodexProviderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "codex-provider-" + Guid.NewGuid().ToString("N"));

    public CodexProviderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-11T00:00:00Z");

    private CodexCredentialsStore FreshStore(string lastRefresh = "2026-06-10T00:00:00Z")
    {
        var path = Path.Combine(_dir, "auth.json");
        File.WriteAllText(path, $$"""
        {"tokens": {"access_token": "tok-1", "refresh_token": "ref-1", "account_id": "acct-1"},
         "last_refresh": "{{lastRefresh}}"}
        """);
        return new CodexCredentialsStore(path);
    }

    private static CodexUsage Usage() => new(
        PlanType: "pro",
        Primary: new CodexWindow(15, DateTimeOffset.FromUnixTimeSeconds(1735401600), 18000),
        Secondary: new CodexWindow(5, DateTimeOffset.FromUnixTimeSeconds(1735920000), 604800));

    [Fact]
    public void ConvertMapsWindowsPlanAndHeadline()
    {
        var snapshot = CodexProvider.Convert(Usage(), Now);
        snapshot.Id.Should().Be(ProviderID.Codex);
        snapshot.Plan.Should().Be("Pro");
        snapshot.Headline.Label.Should().Be("Session · 5h");
        snapshot.Headline.ResetStyle.Should().Be(ResetStyle.Countdown);
        snapshot.Windows.Select(w => w.Label).Should().Equal("Session · 5h", "Weekly");
        snapshot.Windows[1].ResetStyle.Should().Be(ResetStyle.Date);
        snapshot.FetchedAt.Should().Be(Now);
    }

    [Fact]
    public void ConvertFallsBackToSecondaryHeadline()
    {
        var snapshot = CodexProvider.Convert(Usage() with { Primary = null }, Now);
        snapshot.Headline.Label.Should().Be("Weekly");
        snapshot.Windows.Should().HaveCount(1);
    }

    [Fact]
    public void ConvertDerivesLabelsFromDurations()
    {
        var usage = new CodexUsage("plus",
            new CodexWindow(1, null, 21600),
            new CodexWindow(2, null, 14 * 86400));
        var snapshot = CodexProvider.Convert(usage, Now);
        snapshot.Windows.Select(w => w.Label).Should().Equal("Session · 6h", "14-day");
    }

    [Fact]
    public async Task FetchSkipsRefreshWhenStampFresh()
    {
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Is("tok-1"), Arg.Is("acct-1"), Arg.Any<CancellationToken>())
            .Returns(Usage());
        var refresher = Substitute.For<ICodexTokenRefresher>();

        var provider = new CodexProvider(FreshStore(), refresher, endpoint, () => Now);
        var snapshot = await provider.FetchUsageAsync();

        snapshot.Plan.Should().Be("Pro");
        await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    public async Task FetchRefreshesFirstWhenStampStale()
    {
        var store = FreshStore(lastRefresh: "2026-05-01T00:00:00Z");
        var refresher = Substitute.For<ICodexTokenRefresher>();
        refresher.RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CodexCredentials>() with
            {
                AccessToken = "tok-2",
                RefreshToken = "ref-2",
                LastRefresh = Now,
            });
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Is("tok-2"), Arg.Is("acct-1"), Arg.Any<CancellationToken>())
            .Returns(Usage());

        var provider = new CodexProvider(store, refresher, endpoint, () => Now);
        await provider.FetchUsageAsync();

        store.Load().RefreshToken.Should().Be("ref-2", "refreshed tokens must be written back");
    }

    [Fact]
    public async Task FetchOn401RefreshesOnceAndRetries()
    {
        var refresher = Substitute.For<ICodexTokenRefresher>();
        refresher.RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CodexCredentials>() with { AccessToken = "tok-2" });
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new ProviderUnauthorizedException(),
                _ => Task.FromResult(Usage()));

        var provider = new CodexProvider(FreshStore(), refresher, endpoint, () => Now);
        var snapshot = await provider.FetchUsageAsync();

        snapshot.Plan.Should().Be("Pro");
        await endpoint.Received(2).FetchUsageAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchSecond401ThrowsReauthRequired()
    {
        var refresher = Substitute.For<ICodexTokenRefresher>();
        refresher.RefreshAsync(Arg.Any<CodexCredentials>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CodexCredentials>());
        var endpoint = Substitute.For<ICodexUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new ProviderUnauthorizedException());

        var provider = new CodexProvider(FreshStore(), refresher, endpoint, () => Now);
        await provider.Invoking(p => p.FetchUsageAsync())
            .Should().ThrowAsync<ProviderReauthRequiredException>();
    }

    [Fact]
    public void DetectDelegatesToStore()
    {
        var provider = new CodexProvider(
            FreshStore(),
            Substitute.For<ICodexTokenRefresher>(),
            Substitute.For<ICodexUsageEndpoint>(),
            () => Now);
        provider.DetectCredentials().Should().BeTrue();
        provider.Id.Should().Be(ProviderID.Codex);
        provider.DisplayName.Should().Be("Codex");
    }

    [Fact]
    public void DecodeParsesFullPayload()
    {
        var usage = CodexHttpClient.Decode("""
        {
          "plan_type": "pro",
          "rate_limit": {
            "primary_window":   {"used_percent": 15, "reset_at": 1735401600, "limit_window_seconds": 18000},
            "secondary_window": {"used_percent": 5,  "reset_at": 1735920000, "limit_window_seconds": 604800}
          },
          "credits": {"has_credits": true, "unlimited": false, "balance": 150.0}
        }
        """);
        usage.PlanType.Should().Be("pro");
        usage.Primary!.UsedPercent.Should().BeApproximately(15, 0.001);
        usage.Primary.ResetsAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1735401600));
        usage.Primary.WindowSeconds.Should().Be(18000);
        usage.Secondary!.UsedPercent.Should().BeApproximately(5, 0.001);
    }

    [Fact]
    public void DecodeToleratesMissingWindows()
    {
        var usage = CodexHttpClient.Decode(
            """{"plan_type": "plus", "rate_limit": {"secondary_window": {"used_percent": 40}}}""");
        usage.Primary.Should().BeNull();
        usage.Secondary!.UsedPercent.Should().BeApproximately(40, 0.001);
        usage.Secondary.ResetsAt.Should().BeNull();
        usage.Secondary.WindowSeconds.Should().BeNull();
    }

    [Fact]
    public void ConvertThrowsWhenNoWindows()
    {
        var usage = new CodexUsage("pro", null, null);
        FluentActions.Invoking(() => CodexProvider.Convert(usage, Now))
            .Should().Throw<ProviderBadResponseException>();
    }

    [Fact]
    public async Task HttpRefreshKeepsOldRefreshTokenWhenResponseOmitsIt()
    {
        // The live refresher must preserve the stored refresh_token when the
        // refresh response omits one (the endpoint may rotate it or not).
        var handler = new StubHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token": "tok-2"}"""),
        });
        var refresher = new CodexHttpClient(new HttpClient(handler));

        var creds = new CodexCredentials("tok-1", "ref-1", "id-1", "acct-1", null);
        var refreshed = await refresher.RefreshAsync(creds);

        refreshed.AccessToken.Should().Be("tok-2");
        refreshed.RefreshToken.Should().Be("ref-1");
        refreshed.IdToken.Should().Be("id-1");
    }

    [Fact]
    public async Task HttpRefreshRejectionThrowsReauthRequired()
    {
        var handler = new StubHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        var refresher = new CodexHttpClient(new HttpClient(handler));
        var creds = new CodexCredentials("tok-1", "ref-1", null, null, null);

        await refresher.Invoking(r => r.RefreshAsync(creds))
            .Should().ThrowAsync<ProviderReauthRequiredException>();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public StubHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }
}
```

- [ ] **Step 7.2: Run — expect FAIL**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter CodexProviderTests 2>&1 | tail -5`
Expected: compile error — `CodexProvider`/`CodexUsage` not found.

- [ ] **Step 7.3: Implement**

Create `windows/src/TokenSpendie.Windows/Data/CodexProvider.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

/// <summary>One `wham/usage` rate-limit window.</summary>
public sealed record CodexWindow(double UsedPercent, DateTimeOffset? ResetsAt, int? WindowSeconds);

/// <summary>The decoded `wham/usage` payload (credits ignored).</summary>
public sealed record CodexUsage(string? PlanType, CodexWindow? Primary, CodexWindow? Secondary);

public interface ICodexUsageEndpoint
{
    Task<CodexUsage> FetchUsageAsync(string accessToken, string? accountId, CancellationToken ct = default);
}

public interface ICodexTokenRefresher
{
    /// <summary>One refresh round-trip. Throws
    /// <see cref="ProviderReauthRequiredException"/> when the refresh token is
    /// rejected. Fields absent from the response keep their stored values.</summary>
    Task<CodexCredentials> RefreshAsync(CodexCredentials creds, CancellationToken ct = default);
}

/// <summary>
/// The <see cref="IUsageProvider"/> for OpenAI Codex. Reads the CLI's OAuth
/// tokens from <c>auth.json</c>, refreshes them when stale (Codex CLI's own
/// 8-day policy) or on a 401, writes refreshed tokens back (refresh tokens
/// rotate), and calls the `wham/usage` endpoint Codex itself uses.
/// </summary>
public sealed class CodexProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Codex;
    public string DisplayName => "Codex";

    private readonly CodexCredentialsStore _store;
    private readonly ICodexTokenRefresher _refresher;
    private readonly ICodexUsageEndpoint _endpoint;
    private readonly Func<DateTimeOffset> _now;

    public CodexProvider(CodexCredentialsStore store, ICodexTokenRefresher refresher,
                         ICodexUsageEndpoint endpoint, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _refresher = refresher;
        _endpoint = endpoint;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Production wiring. One <see cref="CodexHttpClient"/> serves as
    /// both the refresher and the usage endpoint.</summary>
    public CodexProvider()
    {
        var http = new CodexHttpClient();
        _store = new CodexCredentialsStore();
        _refresher = http;
        _endpoint = http;
        _now = () => DateTimeOffset.UtcNow;
    }

    public bool DetectCredentials() => _store.Detect();

    public async Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var creds = _store.Load();
        if (creds.NeedsRefresh(_now()))
            creds = await RefreshAndSaveAsync(creds, ct).ConfigureAwait(false);
        try
        {
            var usage = await _endpoint.FetchUsageAsync(creds.AccessToken, creds.AccountId, ct)
                .ConfigureAwait(false);
            return Convert(usage, _now());
        }
        catch (ProviderUnauthorizedException)
        {
            creds = await RefreshAndSaveAsync(creds, ct).ConfigureAwait(false);
            try
            {
                var usage = await _endpoint.FetchUsageAsync(creds.AccessToken, creds.AccountId, ct)
                    .ConfigureAwait(false);
                return Convert(usage, _now());
            }
            catch (ProviderUnauthorizedException)
            {
                throw new ProviderReauthRequiredException("Codex");
            }
        }
    }

    private async Task<CodexCredentials> RefreshAndSaveAsync(CodexCredentials creds, CancellationToken ct)
    {
        var refreshed = (await _refresher.RefreshAsync(creds, ct).ConfigureAwait(false))
            with { LastRefresh = _now() };
        _store.Save(refreshed, _now());
        return refreshed;
    }

    /// <summary>Pure mapping. Primary (≈5 h) is the headline; either window may
    /// be absent; neither present → <see cref="ProviderBadResponseException"/>.</summary>
    public static ProviderSnapshot Convert(CodexUsage usage, DateTimeOffset fetchedAt)
    {
        var windows = new List<LabeledWindow>();
        if (usage.Primary is { } primary)
        {
            var hours = Math.Max(1, ((primary.WindowSeconds ?? 18000) + 1800) / 3600);
            windows.Add(new LabeledWindow($"Session · {hours}h", $"{hours}-hour window",
                ResetStyle.Countdown, new UsageWindow(primary.UsedPercent, primary.ResetsAt)));
        }
        if (usage.Secondary is { } secondary)
        {
            var days = Math.Max(1, ((secondary.WindowSeconds ?? 604800) + 43200) / 86400);
            windows.Add(new LabeledWindow(days == 7 ? "Weekly" : $"{days}-day", $"{days}-day window",
                ResetStyle.Date, new UsageWindow(secondary.UsedPercent, secondary.ResetsAt)));
        }
        if (windows.Count == 0)
            throw new ProviderBadResponseException("no rate-limit windows in wham/usage payload");

        var plan = usage.PlanType is { Length: > 0 } raw
            ? string.Join(" ", raw.Split('_').Select(
                part => char.ToUpperInvariant(part[0]) + part[1..]))
            : null;
        return new ProviderSnapshot(
            Id: ProviderID.Codex, Plan: plan,
            Headline: windows[0], Windows: windows,
            FetchedAt: fetchedAt);
    }
}

/// <summary>Production HTTP implementation of both Codex interfaces, following
/// the <see cref="EndpointUsageProvider"/> conventions.</summary>
public sealed class CodexHttpClient : ICodexUsageEndpoint, ICodexTokenRefresher
{
    private static readonly Uri UsageUrl = new("https://chatgpt.com/backend-api/wham/usage");
    private static readonly Uri RefreshUrl = new("https://auth.openai.com/oauth/token");
    /// <summary>Codex CLI's public OAuth client id (from codex-rs source).</summary>
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    private readonly HttpClient _http;

    public CodexHttpClient(HttpClient? http = null) =>
        _http = http ?? EndpointUsageProvider.BuildHttpClient();

    public async Task<CodexUsage> FetchUsageAsync(string accessToken, string? accountId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(accessToken)) throw new ProviderUnauthorizedException();

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("TokenSpendie/1.0");
        if (accountId is not null) request.Headers.Add("ChatGPT-Account-Id", accountId);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderNetworkException(ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (int)response.StatusCode switch
            {
                200 => Decode(body),
                401 or 403 => throw new ProviderUnauthorizedException(),
                429 => throw new ProviderRateLimitedException(response.Headers.RetryAfter?.Delta),
                _ => throw new ProviderBadResponseException($"status {(int)response.StatusCode}"),
            };
        }
    }

    public async Task<CodexCredentials> RefreshAsync(CodexCredentials creds, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                client_id = ClientId,
                grant_type = "refresh_token",
                refresh_token = creds.RefreshToken,
                scope = "openid profile email",
            }), Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderNetworkException(ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ProviderReauthRequiredException("Codex");
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var root = JsonNode.Parse(body);
            return creds with
            {
                AccessToken = root?["access_token"]?.GetValue<string>() ?? creds.AccessToken,
                RefreshToken = root?["refresh_token"]?.GetValue<string>() ?? creds.RefreshToken,
                IdToken = root?["id_token"]?.GetValue<string>() ?? creds.IdToken,
            };
        }
    }

    /// <summary>Public for tests — the JSON edge of the protocol.</summary>
    public static CodexUsage Decode(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (Exception ex)
        {
            throw new ProviderBadResponseException("unparseable wham/usage payload", ex);
        }
        var rateLimit = root?["rate_limit"];

        static CodexWindow? Window(JsonNode? raw)
        {
            if (raw?["used_percent"] is not { } used) return null;
            DateTimeOffset? resetsAt = raw["reset_at"] is { } reset
                ? DateTimeOffset.FromUnixTimeSeconds(reset.GetValue<long>())
                : null;
            return new CodexWindow(used.GetValue<double>(), resetsAt,
                raw["limit_window_seconds"]?.GetValue<int>());
        }

        return new CodexUsage(
            root?["plan_type"]?.GetValue<string>(),
            Window(rateLimit?["primary_window"]),
            Window(rateLimit?["secondary_window"]));
    }
}
```

- [ ] **Step 7.4: Run — expect PASS**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter CodexProviderTests 2>&1 | tail -3`

- [ ] **Step 7.5: Run the whole Windows suite — expect PASS, then commit**

Run: `dotnet test tests/TokenSpendie.Windows.Tests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/Data/CodexProvider.cs windows/tests/TokenSpendie.Windows.Tests/Data/CodexProviderTests.cs
git commit -m "feat(codex,windows): provider with usage fetch, refresh and 401 retry"
```

---

### Task 8: Register on Windows + widget fallthrough

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/App.xaml.cs:42-46`
- Modify: `windows/src/TokenSpendie.WidgetProvider/Data/SnapshotFetcher.cs`

- [ ] **Step 8.1: Register the provider**

In `windows/src/TokenSpendie.Windows/App.xaml.cs`:

```csharp
var providers = new IUsageProvider[]
{
    new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
    new GeminiProvider(),
    new CodexProvider(),
};
```

- [ ] **Step 8.2: Extend the widget-board fallthrough**

In `windows/src/TokenSpendie.WidgetProvider/Data/SnapshotFetcher.cs`, change `GetCurrent()` so Gemini falls through to Codex (Gemini's fetch never throws, so the gate is detection only; Codex gets the same try/catch as Claude):

```csharp
public static UsageSnapshot GetCurrent()
{
    var claude = new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider());
    if (claude.DetectCredentials())
    {
        try
        {
            var t = claude.FetchUsageAsync();
            t.Wait();
            return Convert(t.Result);
        }
        catch
        {
            // Fall through to Gemini.
        }
    }

    var gemini = new GeminiProvider();
    if (gemini.DetectCredentials())
    {
        var t = gemini.FetchUsageAsync();
        t.Wait();
        return Convert(t.Result);
    }

    var codex = new CodexProvider();
    if (codex.DetectCredentials())
    {
        try
        {
            var t = codex.FetchUsageAsync();
            t.Wait();
            return Convert(t.Result);
        }
        catch
        {
            // Fall through to the empty snapshot.
        }
    }

    return Empty();
}
```

- [ ] **Step 8.3: Build + full Windows suite — expect PASS, then commit**

Run: `dotnet build src/TokenSpendie.Windows && dotnet test tests/TokenSpendie.Windows.Tests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/App.xaml.cs windows/src/TokenSpendie.WidgetProvider/Data/SnapshotFetcher.cs
git commit -m "feat(codex,windows): register provider and widget-board fallthrough"
```

---

### Task 9: Finish the branch

- [ ] **Step 9.1: Full verification, both platforms**

Run: `swift test 2>&1 | tail -3` (macOS) and `dotnet test tests/TokenSpendie.Windows.Tests 2>&1 | tail -3` (Windows machine or push and watch `windows-ci.yml`).
Expected: both suites green.

- [ ] **Step 9.2: Manual smoke (macOS)**

`codex login` (if testing live), `./build.sh && open build/TokenSpendie.app`, confirm the CODEX section, Session/Weekly bars, plan pill, and that the menu-bar ring switches when the Codex ring is clicked.

- [ ] **Step 9.3: PR**

Use the superpowers:finishing-a-development-branch skill. PR `feature/codex-provider` → `develop` (protected: PR + 1 approval).
