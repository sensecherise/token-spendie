# Token Spendie Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a native macOS menu bar app that displays Claude Code subscription usage (5-hour session window + weekly caps) in real time, with an optional floating panel.

**Architecture:** A single Swift package producing one `LSUIElement` accessory app. A data layer reads the OAuth token from the login Keychain and fetches `GET /api/oauth/usage`; a `UsageStore` orchestrates polling, caching, and state; two UI hosts (menu bar `NSStatusItem` and a floating `NSPanel`) render from that one store.

**Tech Stack:** Swift 5.9+ / Swift Package Manager, AppKit + SwiftUI, Security (Keychain), ServiceManagement (launch-at-login), Network (reachability). No third-party dependencies. Builds with Command Line Tools — no Xcode required.

---

## Verified facts (from Claude Code 2.1.146 binary)

These were extracted from the installed Claude Code binary and confirmed live by Task 1's probe.

- **Usage endpoint:** `GET https://api.anthropic.com/api/oauth/usage`, headers `Authorization: Bearer <accessToken>` and `Accept: application/json`. Claude Code's internal name is `fetchUtilization`; it retries and, on `401`, refreshes the token and retries.
- **Response shape (confirmed):** keys `five_hour`, `seven_day`, `seven_day_opus`, `seven_day_sonnet` (and other internal windows ignored by this app); each value is `{utilization, resets_at}` or JSON `null`. `utilization` is a **percentage (0–100)**. `resets_at` is ISO 8601 with **microsecond** precision, e.g. `2026-05-21T10:20:00.431249+00:00`. The `anthropic-ratelimit-unified-*` headers are **not** present on this endpoint.
- **Keychain (confirmed):** generic-password item, service `Claude Code-credentials`, in the login keychain. Value is JSON: `{"claudeAiOauth":{"accessToken":"...","refreshToken":"...","expiresAt":<ms>,...}}`. `expiresAt` is epoch **milliseconds**.
- **Token strategy:** the widget never refreshes or rewrites the shared token (that would rotate Claude Code's refresh token and break it). On `401` it re-reads the Keychain once — Claude Code refreshes the token during normal use — and otherwise shows a "login expired" state.

---

## File Structure

```
claude-widget/
├── Package.swift
├── build.sh                                  # compiles + assembles TokenSpendie.app + zip
├── README.md                                 # install/share instructions
├── Resources/
│   └── Info.plist                            # bundle metadata, LSUIElement
├── Tools/
│   ├── probe.swift                           # Task 1: live endpoint/keychain verification
│   └── makeicon.swift                        # draws Resources/AppIcon-1024.png
├── Sources/TokenSpendie/
│   ├── main.swift                            # NSApplication entry point
│   ├── AppDelegate.swift                     # wires everything together
│   ├── Model/
│   │   └── UsageModels.swift                 # UsageWindow, ModelWeekly, UsageSnapshot, errors, LoadState
│   ├── Data/
│   │   ├── OAuthCredentials.swift            # credentials struct + parser + CredentialStore protocol
│   │   ├── KeychainReader.swift              # real CredentialStore over the macOS Keychain
│   │   ├── UsageDecoder.swift                # /api/oauth/usage JSON -> UsageSnapshot
│   │   ├── UsageProvider.swift               # protocol + HTTPTransport typealias
│   │   └── EndpointUsageProvider.swift       # /api/oauth/usage implementation
│   ├── Store/
│   │   ├── SnapshotCache.swift               # last snapshot persisted to disk
│   │   ├── Preferences.swift                 # UserDefaults-backed settings
│   │   └── UsageStore.swift                  # timer, orchestration, published state
│   └── UI/
│       ├── Formatting.swift                  # level/color + reset-time strings
│       ├── RingView.swift                    # session ring + compact menu bar label
│       ├── DetailPanelView.swift             # stacked-bar detail view
│       ├── PreferencesView.swift             # settings UI + launch-at-login
│       ├── MenuBarController.swift           # NSStatusItem + popover
│       └── FloatingPanelController.swift     # always-on-top NSPanel
└── Tests/TokenSpendieTests/
    ├── UsageModelsTests.swift
    ├── OAuthCredentialsTests.swift
    ├── KeychainReaderTests.swift
    ├── UsageDecoderTests.swift
    ├── UsageProviderTests.swift
    ├── SnapshotCacheTests.swift
    ├── PreferencesTests.swift
    ├── FormattingTests.swift
    └── UsageStoreTests.swift
```

Each file has one responsibility. Pure-logic files (everything in `Model/`, `Data/`, `Store/`, plus `Formatting.swift`) are unit-tested. UI files are verified by `swift build` plus manual runtime QA in Tasks 17 and 18, because the app cannot fully launch until the wiring task.

---

## A note on `swift` invocations

The Swift toolchain is installed via Command Line Tools but is not on the default `PATH` in all shells. If `swift` is not found, prefix commands with the toolchain path:

```bash
export PATH="/Library/Developer/CommandLineTools/usr/bin:$PATH"
```

All `swift build` / `swift test` / `swift Tools/*.swift` commands below assume `swift` resolves.

---

## Task 1: Live verification probe

Confirms the endpoint, headers, response schema, and Keychain blob against the real account before any code depends on them. The probe prints no secret token values.

**Files:**
- Create: `Tools/probe.swift`

- [ ] **Step 1: Write the probe script**

```swift
// Tools/probe.swift — run with: swift Tools/probe.swift
// Verifies the Keychain item and the /api/oauth/usage endpoint. Prints no token values.
import Foundation
import Security

func keychainBlob() -> Data? {
    let query: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: "Claude Code-credentials",
        kSecReturnData as String: true,
        kSecMatchLimit as String: kSecMatchLimitOne,
    ]
    var out: AnyObject?
    let status = SecItemCopyMatching(query as CFDictionary, &out)
    guard status == errSecSuccess else {
        FileHandle.standardError.write(Data("SecItemCopyMatching status: \(status)\n".utf8))
        return nil
    }
    return out as? Data
}

guard let blob = keychainBlob() else {
    print("No Keychain item — is Claude Code installed and logged in?")
    exit(1)
}
guard let root = try? JSONSerialization.jsonObject(with: blob) as? [String: Any],
      let oauth = root["claudeAiOauth"] as? [String: Any],
      let token = oauth["accessToken"] as? String else {
    print("Could not parse claudeAiOauth.accessToken from the Keychain blob")
    exit(1)
}
print("Keychain oauth keys: \(oauth.keys.sorted())")
print("expiresAt raw value: \(oauth["expiresAt"] ?? "nil")")
print("accessToken length: \(token.count)")

var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
request.httpMethod = "GET"
request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
request.setValue("application/json", forHTTPHeaderField: "Accept")
request.setValue("TokenSpendie/probe", forHTTPHeaderField: "User-Agent")

let semaphore = DispatchSemaphore(value: 0)
URLSession.shared.dataTask(with: request) { data, response, error in
    defer { semaphore.signal() }
    if let error = error { print("Network error: \(error)"); return }
    let http = response as! HTTPURLResponse
    print("HTTP status: \(http.statusCode)")
    print("anthropic-ratelimit-unified-status: \(http.value(forHTTPHeaderField: "anthropic-ratelimit-unified-status") ?? "—")")
    print("anthropic-ratelimit-unified-reset: \(http.value(forHTTPHeaderField: "anthropic-ratelimit-unified-reset") ?? "—")")
    if let data = data, let body = String(data: data, encoding: .utf8) {
        print("Response body:\n\(body)")
    }
}.resume()
semaphore.wait()
```

- [ ] **Step 2: Run the probe**

Run: `swift Tools/probe.swift`
Expected: a macOS Keychain prompt appears ("…wants to use information stored in Claude Code-credentials") — click **Allow**. Then the script prints the Keychain key list, the HTTP status, and the JSON body.

- [ ] **Step 3: Record the findings**

Confirm and write down (these feed Tasks 4 and 6):
1. HTTP status is `200`. If `401`, the access token is stale — run any `claude` command to refresh, then re-run.
2. The body contains objects keyed `five_hour` and `seven_day` (and likely `seven_day_opus` / `seven_day_sonnet`), each with `utilization` and `resets_at`.
3. Whether `utilization` is a fraction (`0`–`1`) or a percentage (`0`–`100`). The decoder in Task 6 normalizes both, but note the actual scale.
4. The `resets_at` string format (expected ISO 8601, e.g. `2026-05-25T07:00:00Z`).
5. Whether `expiresAt` in the Keychain looks like seconds (~10 digits) or milliseconds (~13 digits).

If the response shape differs materially from the above, adjust the `UsageDecoder` code in Task 6 to match what the probe actually returned. Do not proceed with a guessed schema.

- [ ] **Step 4: Commit**

```bash
git add Tools/probe.swift
git commit -m "Add live verification probe for usage endpoint and Keychain"
```

---

## Task 2: Package scaffold

Creates the Swift package so `swift build` and `swift test` succeed with an empty app.

**Files:**
- Create: `Package.swift`
- Create: `Sources/TokenSpendie/main.swift`
- Create: `Sources/TokenSpendie/AppDelegate.swift`
- Create: `Tests/TokenSpendieTests/UsageModelsTests.swift`

- [ ] **Step 1: Write `Package.swift`**

```swift
// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "TokenSpendie",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(name: "TokenSpendie", path: "Sources/TokenSpendie"),
        .testTarget(
            name: "TokenSpendieTests",
            dependencies: ["TokenSpendie"],
            path: "Tests/TokenSpendieTests"
        ),
    ]
)
```

- [ ] **Step 2: Write a minimal `AppDelegate.swift`**

```swift
import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        // Wiring is added in Task 17.
    }
}
```

- [ ] **Step 3: Write `main.swift`**

```swift
import AppKit

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate()
app.delegate = delegate
app.run()
```

- [ ] **Step 4: Write a placeholder test so the test target builds**

```swift
import XCTest

final class UsageModelsTests: XCTestCase {
    func testScaffold() {
        XCTAssertTrue(true)
    }
}
```

- [ ] **Step 5: Build and test**

Run: `swift build`
Expected: `Build complete!`
Run: `swift test`
Expected: `Test Suite 'All tests' passed`.

- [ ] **Step 6: Commit**

```bash
git add Package.swift Sources Tests
git commit -m "Scaffold Swift package for Token Spendie"
```

---

## Task 3: Usage models

Defines the data types shared across every layer.

**Files:**
- Create: `Sources/TokenSpendie/Model/UsageModels.swift`
- Modify: `Tests/TokenSpendieTests/UsageModelsTests.swift`

- [ ] **Step 1: Write the failing test**

Replace the contents of `UsageModelsTests.swift`:

```swift
import XCTest
@testable import TokenSpendie

final class UsageModelsTests: XCTestCase {
    func testSnapshotCodableRoundTrip() throws {
        let snapshot = UsageSnapshot(
            session: UsageWindow(percent: 52, resetsAt: Date(timeIntervalSince1970: 1_000_000)),
            weekly: UsageWindow(percent: 74, resetsAt: Date(timeIntervalSince1970: 2_000_000)),
            modelWeeklies: [ModelWeekly(model: "Opus", window: UsageWindow(percent: 91, resetsAt: nil))],
            fetchedAt: Date(timeIntervalSince1970: 3_000_000)
        )
        let data = try JSONEncoder().encode(snapshot)
        let decoded = try JSONDecoder().decode(UsageSnapshot.self, from: data)
        XCTAssertEqual(snapshot, decoded)
    }

    func testLoadStateEquatable() {
        XCTAssertEqual(LoadState.error(.network), LoadState.error(.network))
        XCTAssertNotEqual(LoadState.error(.network), LoadState.error(.loginExpired))
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter UsageModelsTests`
Expected: FAIL — `cannot find 'UsageSnapshot' in scope`.

- [ ] **Step 3: Write `UsageModels.swift`**

```swift
import Foundation

/// A single rate-limit window (session or weekly).
struct UsageWindow: Codable, Equatable {
    /// Percentage used, normalized to 0–100 (may exceed 100 if over cap).
    let percent: Double
    /// When this window's usage resets, if known.
    let resetsAt: Date?
}

/// A model-specific weekly cap (e.g. Opus, Sonnet).
struct ModelWeekly: Codable, Equatable {
    let model: String
    let window: UsageWindow
}

/// One complete reading of usage at a point in time.
struct UsageSnapshot: Codable, Equatable {
    let session: UsageWindow          // five_hour
    let weekly: UsageWindow           // seven_day
    let modelWeeklies: [ModelWeekly]  // seven_day_opus / seven_day_sonnet, if present
    let fetchedAt: Date
}

/// A user-facing failure. Each maps to a distinct widget state.
enum UsageError: Error, Equatable {
    case claudeCodeNotFound     // no Keychain item
    case keychainAccessDenied   // user denied Keychain access
    case loginExpired           // 401 even after re-reading the Keychain
    case network                // offline / unreachable
    case badResponse            // non-200 or unparseable payload
}

/// What the data layer's provider/decoder can throw.
enum ProviderError: Error, Equatable {
    case unauthorized           // HTTP 401
    case network                // transport failure
    case badResponse            // non-200, or payload could not be decoded
}

/// The store's published display state.
enum LoadState: Equatable {
    case loading                // first load, no snapshot yet
    case ok                     // showing a fresh snapshot
    case stale                  // showing a cached snapshot, last refresh failed
    case error(UsageError)      // no usable snapshot to show
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter UsageModelsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Model Tests/TokenSpendieTests/UsageModelsTests.swift
git commit -m "Add usage data models"
```

---

## Task 4: OAuth credentials + parser

Models the Keychain credentials and parses the `claudeAiOauth` JSON blob. The parser is separate from Keychain access so it is fully unit-testable.

**Files:**
- Create: `Sources/TokenSpendie/Data/OAuthCredentials.swift`
- Create: `Tests/TokenSpendieTests/OAuthCredentialsTests.swift`

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

final class OAuthCredentialsTests: XCTestCase {
    func testParsesSecondsExpiry() throws {
        let json = #"{"claudeAiOauth":{"accessToken":"abc","refreshToken":"ref","expiresAt":1700000000}}"#
        let creds = try OAuthCredentialsParser.parse(Data(json.utf8))
        XCTAssertEqual(creds.accessToken, "abc")
        XCTAssertEqual(creds.refreshToken, "ref")
        XCTAssertEqual(creds.expiresAt, Date(timeIntervalSince1970: 1_700_000_000))
    }

    func testParsesMillisecondsExpiry() throws {
        let json = #"{"claudeAiOauth":{"accessToken":"abc","expiresAt":1700000000000}}"#
        let creds = try OAuthCredentialsParser.parse(Data(json.utf8))
        XCTAssertEqual(creds.expiresAt, Date(timeIntervalSince1970: 1_700_000_000))
        XCTAssertNil(creds.refreshToken)
    }

    func testMissingAccessTokenThrowsMalformed() {
        let json = #"{"claudeAiOauth":{"refreshToken":"ref"}}"#
        XCTAssertThrowsError(try OAuthCredentialsParser.parse(Data(json.utf8))) { error in
            XCTAssertEqual(error as? CredentialError, .malformed)
        }
    }

    func testGarbageThrowsMalformed() {
        XCTAssertThrowsError(try OAuthCredentialsParser.parse(Data("not json".utf8))) { error in
            XCTAssertEqual(error as? CredentialError, .malformed)
        }
    }

    func testIsExpired() {
        let past = OAuthCredentials(accessToken: "a", refreshToken: nil,
                                    expiresAt: Date(timeIntervalSince1970: 100))
        XCTAssertTrue(past.isExpired(now: Date(timeIntervalSince1970: 200)))
        XCTAssertFalse(past.isExpired(now: Date(timeIntervalSince1970: 50)))
        let noExpiry = OAuthCredentials(accessToken: "a", refreshToken: nil, expiresAt: nil)
        XCTAssertFalse(noExpiry.isExpired(now: Date()))
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter OAuthCredentialsTests`
Expected: FAIL — `cannot find 'OAuthCredentialsParser' in scope`.

- [ ] **Step 3: Write `OAuthCredentials.swift`**

```swift
import Foundation

/// Decoded OAuth credentials from the Claude Code Keychain item.
struct OAuthCredentials: Equatable {
    let accessToken: String
    let refreshToken: String?
    let expiresAt: Date?

    func isExpired(now: Date) -> Bool {
        guard let expiresAt else { return false }
        return now >= expiresAt
    }
}

/// Failure modes for obtaining credentials.
enum CredentialError: Error, Equatable {
    case notFound       // no Keychain item
    case accessDenied   // user denied / cancelled Keychain access
    case malformed      // item exists but JSON could not be parsed
}

/// Parses the `claudeAiOauth` JSON blob. Separated from Keychain I/O for testability.
enum OAuthCredentialsParser {
    static func parse(_ data: Data) throws -> OAuthCredentials {
        guard
            let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
            let oauth = root["claudeAiOauth"] as? [String: Any],
            let accessToken = oauth["accessToken"] as? String, !accessToken.isEmpty
        else {
            throw CredentialError.malformed
        }
        let refreshToken = oauth["refreshToken"] as? String
        var expiresAt: Date?
        if let raw = (oauth["expiresAt"] as? NSNumber)?.doubleValue {
            // Heuristic: values past year ~2001 in ms are > 1e12; treat those as milliseconds.
            let seconds = raw > 1_000_000_000_000 ? raw / 1000.0 : raw
            expiresAt = Date(timeIntervalSince1970: seconds)
        }
        return OAuthCredentials(accessToken: accessToken, refreshToken: refreshToken, expiresAt: expiresAt)
    }
}

/// Abstraction over credential storage so the store can be tested without the Keychain.
protocol CredentialStore {
    func loadCredentials() throws -> OAuthCredentials
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter OAuthCredentialsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Data/OAuthCredentials.swift Tests/TokenSpendieTests/OAuthCredentialsTests.swift
git commit -m "Add OAuth credentials model and parser"
```

---

## Task 5: KeychainReader

The real `CredentialStore` over the macOS Keychain. The not-found path is unit-testable by querying a service name that cannot exist.

**Files:**
- Create: `Sources/TokenSpendie/Data/KeychainReader.swift`
- Create: `Tests/TokenSpendieTests/KeychainReaderTests.swift`

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

final class KeychainReaderTests: XCTestCase {
    func testMissingItemThrowsNotFound() {
        let reader = KeychainReader(service: "TokenSpendie-NoSuchItem-\(UUID().uuidString)")
        XCTAssertThrowsError(try reader.loadCredentials()) { error in
            XCTAssertEqual(error as? CredentialError, .notFound)
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter KeychainReaderTests`
Expected: FAIL — `cannot find 'KeychainReader' in scope`.

- [ ] **Step 3: Write `KeychainReader.swift`**

```swift
import Foundation
import Security

/// Reads the Claude Code OAuth credentials from the login Keychain.
struct KeychainReader: CredentialStore {
    let service: String

    init(service: String = "Claude Code-credentials") {
        self.service = service
    }

    func loadCredentials() throws -> OAuthCredentials {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)

        switch status {
        case errSecSuccess:
            guard let data = result as? Data else { throw CredentialError.malformed }
            return try OAuthCredentialsParser.parse(data)
        case errSecItemNotFound:
            throw CredentialError.notFound
        case errSecAuthFailed, errSecUserCanceled, errSecInteractionNotAllowed:
            throw CredentialError.accessDenied
        default:
            throw CredentialError.accessDenied
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter KeychainReaderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Data/KeychainReader.swift Tests/TokenSpendieTests/KeychainReaderTests.swift
git commit -m "Add KeychainReader credential store"
```

---

## Task 6: UsageDecoder

Decodes the `/api/oauth/usage` JSON response into a `UsageSnapshot`. Uses `JSONSerialization` for leniency toward missing/unknown keys.

**Files:**
- Create: `Sources/TokenSpendie/Data/UsageDecoder.swift`
- Create: `Tests/TokenSpendieTests/UsageDecoderTests.swift`

> Confirmed by Task 1's probe: each window is `{utilization, resets_at}`; `utilization` is a percentage (0–100); `resets_at` is ISO 8601 with microsecond precision (e.g. `2026-05-21T10:20:00.431249+00:00`); windows that do not apply are sent as JSON `null`.

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

final class UsageDecoderTests: XCTestCase {
    let fetchedAt = Date(timeIntervalSince1970: 1_700_000_000)

    func testDecodesPercentUtilization() throws {
        // Real endpoint payload shape: utilization is a percentage; resets_at has microseconds.
        let json = """
        {
          "five_hour":      {"utilization": 44.0, "resets_at": "2026-05-21T10:20:00.431249+00:00"},
          "seven_day":      {"utilization": 8.0,  "resets_at": "2026-05-26T08:00:00.431273+00:00"},
          "seven_day_opus": {"utilization": 91.0, "resets_at": "2026-05-26T08:00:00.431280+00:00"}
        }
        """
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        XCTAssertEqual(snapshot.session.percent, 44, accuracy: 0.001)
        XCTAssertEqual(snapshot.weekly.percent, 8, accuracy: 0.001)
        XCTAssertEqual(snapshot.modelWeeklies.count, 1)
        XCTAssertEqual(snapshot.modelWeeklies.first?.model, "Opus")
        XCTAssertEqual(snapshot.modelWeeklies.first?.window.percent ?? 0, 91, accuracy: 0.001)
        XCTAssertEqual(snapshot.fetchedAt, fetchedAt)
    }

    func testParsesMicrosecondResetTime() throws {
        let json = #"{"five_hour":{"utilization":1,"resets_at":"2026-05-21T10:20:00.431249+00:00"},"seven_day":{"utilization":1}}"#
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        let resetsAt = try XCTUnwrap(snapshot.session.resetsAt)
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = TimeZone(identifier: "UTC")!
        let parts = utc.dateComponents([.year, .month, .day, .hour, .minute], from: resetsAt)
        XCTAssertEqual(parts.year, 2026)
        XCTAssertEqual(parts.month, 5)
        XCTAssertEqual(parts.day, 21)
        XCTAssertEqual(parts.hour, 10)
        XCTAssertEqual(parts.minute, 20)
    }

    func testNullWindowIsOmitted() throws {
        // The endpoint sends explicit JSON null for windows that do not apply.
        let json = """
        {"five_hour":{"utilization":5},"seven_day":{"utilization":6},"seven_day_opus":null,"seven_day_sonnet":{"utilization":7}}
        """
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        XCTAssertEqual(snapshot.modelWeeklies.map(\.model), ["Sonnet"])
    }

    func testDecodesOpusAndSonnetWeekly() throws {
        let json = """
        {
          "five_hour": {"utilization": 1}, "seven_day": {"utilization": 2},
          "seven_day_opus": {"utilization": 3}, "seven_day_sonnet": {"utilization": 4}
        }
        """
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        XCTAssertEqual(snapshot.modelWeeklies.map(\.model), ["Opus", "Sonnet"])
    }

    func testMissingRequiredWindowThrowsBadResponse() {
        let json = #"{"five_hour": {"utilization": 5}}"#
        XCTAssertThrowsError(try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)) { error in
            XCTAssertEqual(error as? ProviderError, .badResponse)
        }
    }

    func testGarbageThrowsBadResponse() {
        XCTAssertThrowsError(try UsageDecoder.decode(Data("nonsense".utf8), fetchedAt: fetchedAt)) { error in
            XCTAssertEqual(error as? ProviderError, .badResponse)
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter UsageDecoderTests`
Expected: FAIL — `cannot find 'UsageDecoder' in scope`.

- [ ] **Step 3: Write `UsageDecoder.swift`**

```swift
import Foundation

/// Decodes the `/api/oauth/usage` JSON payload into a `UsageSnapshot`.
enum UsageDecoder {
    static func decode(_ data: Data, fetchedAt: Date) throws -> UsageSnapshot {
        guard let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            throw ProviderError.badResponse
        }

        func window(_ key: String) -> UsageWindow? {
            // A window key may be absent or an explicit JSON null; both mean "not present".
            guard let raw = root[key] as? [String: Any],
                  let utilization = (raw["utilization"] as? NSNumber)?.doubleValue else {
                return nil
            }
            // The endpoint reports utilization directly as a percentage (0–100).
            let resetsAt = (raw["resets_at"] as? String).flatMap(parseDate)
            return UsageWindow(percent: utilization, resetsAt: resetsAt)
        }

        guard let session = window("five_hour"), let weekly = window("seven_day") else {
            throw ProviderError.badResponse
        }

        var modelWeeklies: [ModelWeekly] = []
        if let opus = window("seven_day_opus") {
            modelWeeklies.append(ModelWeekly(model: "Opus", window: opus))
        }
        if let sonnet = window("seven_day_sonnet") {
            modelWeeklies.append(ModelWeekly(model: "Sonnet", window: sonnet))
        }

        return UsageSnapshot(session: session, weekly: weekly,
                             modelWeeklies: modelWeeklies, fetchedAt: fetchedAt)
    }

    /// Parses the endpoint's ISO 8601 timestamps. The endpoint emits microsecond
    /// precision (e.g. "2026-05-21T10:20:00.431249+00:00"), which `ISO8601DateFormatter`
    /// will not accept, so the fractional-seconds component is stripped before parsing.
    static func parseDate(_ string: String) -> Date? {
        let withoutFraction = string.replacingOccurrences(
            of: #"\.\d+"#, with: "", options: .regularExpression)
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: withoutFraction)
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter UsageDecoderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Data/UsageDecoder.swift Tests/TokenSpendieTests/UsageDecoderTests.swift
git commit -m "Add usage response decoder"
```

---

## Task 7: Usage provider

The `UsageProvider` protocol and its endpoint implementation. HTTP is injected as a closure so the provider is unit-testable without real network.

> **Plan change (from Task 1):** the plan originally included a second `HeaderUsageProvider` reading `anthropic-ratelimit-unified-*` response headers as a fallback. Task 1's probe proved those headers are absent from `/api/oauth/usage`, so that concrete fallback is dropped. The `UsageProvider` protocol still provides the swappable seam the spec asked for — a future fallback can be dropped in without touching callers.

**Files:**
- Create: `Sources/TokenSpendie/Data/UsageProvider.swift`
- Create: `Sources/TokenSpendie/Data/EndpointUsageProvider.swift`
- Create: `Tests/TokenSpendieTests/UsageProviderTests.swift`

- [ ] **Step 1: Write `UsageProvider.swift` (protocol + transport)**

```swift
import Foundation

/// Injected HTTP transport: performs a request and returns the body + HTTP response.
typealias HTTPTransport = (URLRequest) async throws -> (Data, HTTPURLResponse)

/// Default transport backed by `URLSession`.
enum DefaultTransport {
    static let shared: HTTPTransport = { request in
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse else { throw ProviderError.network }
            return (data, http)
        } catch let error as ProviderError {
            throw error
        } catch {
            throw ProviderError.network
        }
    }
}

/// Fetches a usage snapshot given a valid access token.
protocol UsageProvider {
    func fetchUsage(accessToken: String) async throws -> UsageSnapshot
}
```

- [ ] **Step 2: Write `EndpointUsageProvider.swift`**

```swift
import Foundation

/// Fetches usage from the dedicated `/api/oauth/usage` endpoint.
struct EndpointUsageProvider: UsageProvider {
    private let transport: HTTPTransport
    private let now: () -> Date
    private let url = URL(string: "https://api.anthropic.com/api/oauth/usage")!

    init(transport: @escaping HTTPTransport = DefaultTransport.shared,
         now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.now = now
    }

    func fetchUsage(accessToken: String) async throws -> UsageSnapshot {
        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("TokenSpendie/1.0", forHTTPHeaderField: "User-Agent")

        let (data, response) = try await transport(request)
        switch response.statusCode {
        case 200:
            return try UsageDecoder.decode(data, fetchedAt: now())
        case 401:
            throw ProviderError.unauthorized
        default:
            throw ProviderError.badResponse
        }
    }
}
```

- [ ] **Step 3: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

final class UsageProviderTests: XCTestCase {
    private func http(_ status: Int) -> HTTPURLResponse {
        HTTPURLResponse(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!,
                        statusCode: status, httpVersion: nil, headerFields: [:])!
    }

    func testEndpointProviderDecodes200() async throws {
        let body = Data(#"{"five_hour":{"utilization":50},"seven_day":{"utilization":60}}"#.utf8)
        let provider = EndpointUsageProvider(
            transport: { _ in (body, self.http(200)) },
            now: { Date(timeIntervalSince1970: 42) }
        )
        let snapshot = try await provider.fetchUsage(accessToken: "tok")
        XCTAssertEqual(snapshot.session.percent, 50, accuracy: 0.001)
        XCTAssertEqual(snapshot.weekly.percent, 60, accuracy: 0.001)
        XCTAssertEqual(snapshot.fetchedAt, Date(timeIntervalSince1970: 42))
    }

    func testEndpointProvider401ThrowsUnauthorized() async {
        let provider = EndpointUsageProvider(transport: { _ in (Data(), self.http(401)) })
        await assertThrows(provider, .unauthorized)
    }

    func testEndpointProvider500ThrowsBadResponse() async {
        let provider = EndpointUsageProvider(transport: { _ in (Data(), self.http(500)) })
        await assertThrows(provider, .badResponse)
    }

    func testEndpointProviderSendsBearerHeader() async throws {
        var captured: URLRequest?
        let body = Data(#"{"five_hour":{"utilization":10},"seven_day":{"utilization":10}}"#.utf8)
        let provider = EndpointUsageProvider(transport: { request in
            captured = request
            return (body, self.http(200))
        })
        _ = try await provider.fetchUsage(accessToken: "secret-token")
        XCTAssertEqual(captured?.value(forHTTPHeaderField: "Authorization"), "Bearer secret-token")
        XCTAssertEqual(captured?.value(forHTTPHeaderField: "Accept"), "application/json")
    }

    private func assertThrows(_ provider: UsageProvider, _ expected: ProviderError,
                              file: StaticString = #filePath, line: UInt = #line) async {
        do {
            _ = try await provider.fetchUsage(accessToken: "tok")
            XCTFail("expected \(expected)", file: file, line: line)
        } catch {
            XCTAssertEqual(error as? ProviderError, expected, file: file, line: line)
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter UsageProviderTests`
Expected: PASS (the implementations from Steps 1–2 are already in place).

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Data/UsageProvider.swift Sources/TokenSpendie/Data/EndpointUsageProvider.swift Tests/TokenSpendieTests/UsageProviderTests.swift
git commit -m "Add endpoint usage provider"
```

---

## Task 8: SnapshotCache

Persists the last snapshot to disk so the widget shows last-known values immediately on relaunch.

**Files:**
- Create: `Sources/TokenSpendie/Store/SnapshotCache.swift`
- Create: `Tests/TokenSpendieTests/SnapshotCacheTests.swift`

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

final class SnapshotCacheTests: XCTestCase {
    private func tempURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("cache-\(UUID().uuidString).json")
    }

    private let sample = UsageSnapshot(
        session: UsageWindow(percent: 30, resetsAt: nil),
        weekly: UsageWindow(percent: 60, resetsAt: nil),
        modelWeeklies: [],
        fetchedAt: Date(timeIntervalSince1970: 555)
    )

    func testLoadReturnsNilWhenAbsent() {
        let cache = SnapshotCache(fileURL: tempURL())
        XCTAssertNil(cache.load())
    }

    func testSaveThenLoadRoundTrips() {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url) }
        let cache = SnapshotCache(fileURL: url)
        cache.save(sample)
        XCTAssertEqual(cache.load(), sample)
    }

    func testLoadReturnsNilForCorruptFile() throws {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url) }
        try Data("corrupt".utf8).write(to: url)
        XCTAssertNil(SnapshotCache(fileURL: url).load())
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter SnapshotCacheTests`
Expected: FAIL — `cannot find 'SnapshotCache' in scope`.

- [ ] **Step 3: Write `SnapshotCache.swift`**

```swift
import Foundation

/// Persists the most recent `UsageSnapshot` to a JSON file.
struct SnapshotCache {
    let fileURL: URL

    /// Default location: ~/Library/Application Support/TokenSpendie/last-snapshot.json
    static func defaultURL() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("TokenSpendie", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("last-snapshot.json")
    }

    func load() -> UsageSnapshot? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return try? JSONDecoder().decode(UsageSnapshot.self, from: data)
    }

    func save(_ snapshot: UsageSnapshot) {
        guard let data = try? JSONEncoder().encode(snapshot) else { return }
        try? data.write(to: fileURL, options: .atomic)
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter SnapshotCacheTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Store/SnapshotCache.swift Tests/TokenSpendieTests/SnapshotCacheTests.swift
git commit -m "Add on-disk snapshot cache"
```

---

## Task 9: Preferences

UserDefaults-backed settings: which surfaces are shown, refresh interval, launch-at-login.

**Files:**
- Create: `Sources/TokenSpendie/Store/Preferences.swift`
- Create: `Tests/TokenSpendieTests/PreferencesTests.swift`

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

final class PreferencesTests: XCTestCase {
    private func freshDefaults() -> UserDefaults {
        let suite = "prefs-test-\(UUID().uuidString)"
        return UserDefaults(suiteName: suite)!
    }

    @MainActor
    func testDefaultsWhenUnset() {
        let prefs = Preferences(defaults: freshDefaults())
        XCTAssertTrue(prefs.showMenuBar)
        XCTAssertFalse(prefs.showFloatingPanel)
        XCTAssertEqual(prefs.refreshInterval, .s60)
        XCTAssertFalse(prefs.launchAtLogin)
    }

    @MainActor
    func testValuesPersist() {
        let defaults = freshDefaults()
        let first = Preferences(defaults: defaults)
        first.showFloatingPanel = true
        first.refreshInterval = .s30
        let second = Preferences(defaults: defaults)
        XCTAssertTrue(second.showFloatingPanel)
        XCTAssertEqual(second.refreshInterval, .s30)
    }

    func testRefreshIntervalSeconds() {
        XCTAssertEqual(RefreshInterval.s30.seconds, 30)
        XCTAssertEqual(RefreshInterval.s60.seconds, 60)
        XCTAssertEqual(RefreshInterval.s120.seconds, 120)
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter PreferencesTests`
Expected: FAIL — `cannot find 'Preferences' in scope`.

- [ ] **Step 3: Write `Preferences.swift`**

```swift
import Foundation
import Combine

/// How often the widget polls when no panel is open.
enum RefreshInterval: Int, CaseIterable, Identifiable {
    case s30 = 30
    case s60 = 60
    case s120 = 120

    var id: Int { rawValue }
    var seconds: TimeInterval { TimeInterval(rawValue) }
    var label: String {
        switch self {
        case .s30: return "30 seconds"
        case .s60: return "60 seconds"
        case .s120: return "2 minutes"
        }
    }
}

/// Observable, UserDefaults-backed preferences.
@MainActor
final class Preferences: ObservableObject {
    private let defaults: UserDefaults

    @Published var showMenuBar: Bool { didSet { defaults.set(showMenuBar, forKey: Keys.showMenuBar) } }
    @Published var showFloatingPanel: Bool { didSet { defaults.set(showFloatingPanel, forKey: Keys.showFloatingPanel) } }
    @Published var refreshInterval: RefreshInterval { didSet { defaults.set(refreshInterval.rawValue, forKey: Keys.refreshInterval) } }
    @Published var launchAtLogin: Bool { didSet { defaults.set(launchAtLogin, forKey: Keys.launchAtLogin) } }

    private enum Keys {
        static let showMenuBar = "showMenuBar"
        static let showFloatingPanel = "showFloatingPanel"
        static let refreshInterval = "refreshInterval"
        static let launchAtLogin = "launchAtLogin"
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        self.showMenuBar = defaults.object(forKey: Keys.showMenuBar) as? Bool ?? true
        self.showFloatingPanel = defaults.object(forKey: Keys.showFloatingPanel) as? Bool ?? false
        let storedInterval = defaults.object(forKey: Keys.refreshInterval) as? Int ?? RefreshInterval.s60.rawValue
        self.refreshInterval = RefreshInterval(rawValue: storedInterval) ?? .s60
        self.launchAtLogin = defaults.object(forKey: Keys.launchAtLogin) as? Bool ?? false
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter PreferencesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Store/Preferences.swift Tests/TokenSpendieTests/PreferencesTests.swift
git commit -m "Add UserDefaults-backed preferences"
```

---

## Task 10: Formatting

Pure presentation helpers: usage level (color tier) and human-readable time strings.

**Files:**
- Create: `Sources/TokenSpendie/UI/Formatting.swift`
- Create: `Tests/TokenSpendieTests/FormattingTests.swift`

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

final class FormattingTests: XCTestCase {
    func testLevelTiers() {
        XCTAssertEqual(UsageLevel.forPercent(0), .calm)
        XCTAssertEqual(UsageLevel.forPercent(69.9), .calm)
        XCTAssertEqual(UsageLevel.forPercent(70), .warn)
        XCTAssertEqual(UsageLevel.forPercent(89.9), .warn)
        XCTAssertEqual(UsageLevel.forPercent(90), .hot)
        XCTAssertEqual(UsageLevel.forPercent(150), .hot)
    }

    func testResetCountdown() {
        let now = Date(timeIntervalSince1970: 0)
        let in2h47m = Date(timeIntervalSince1970: 2 * 3600 + 47 * 60)
        XCTAssertEqual(Formatting.resetCountdown(to: in2h47m, now: now), "resets in 2h 47m")
        let in40m = Date(timeIntervalSince1970: 40 * 60)
        XCTAssertEqual(Formatting.resetCountdown(to: in40m, now: now), "resets in 40m")
        XCTAssertEqual(Formatting.resetCountdown(to: now, now: now), "resetting now")
        XCTAssertEqual(Formatting.resetCountdown(to: nil, now: now), "")
    }

    func testUpdatedAgo() {
        let now = Date(timeIntervalSince1970: 1000)
        XCTAssertEqual(Formatting.updatedAgo(Date(timeIntervalSince1970: 990), now: now), "updated 10s ago")
        XCTAssertEqual(Formatting.updatedAgo(Date(timeIntervalSince1970: 700), now: now), "updated 5m ago")
        XCTAssertEqual(Formatting.updatedAgo(now, now: now), "updated just now")
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter FormattingTests`
Expected: FAIL — `cannot find 'UsageLevel' in scope`.

- [ ] **Step 3: Write `Formatting.swift`**

```swift
import Foundation
import SwiftUI

/// Color tier for a usage percentage.
enum UsageLevel {
    case calm   // < 70%
    case warn   // 70–90%
    case hot    // > 90%

    static func forPercent(_ percent: Double) -> UsageLevel {
        if percent >= 90 { return .hot }
        if percent >= 70 { return .warn }
        return .calm
    }

    var color: Color {
        switch self {
        case .calm: return Color(red: 0.37, green: 0.72, blue: 0.47)
        case .warn: return Color(red: 0.88, green: 0.64, blue: 0.25)
        case .hot:  return Color(red: 0.85, green: 0.33, blue: 0.31)
        }
    }
}

/// Pure string formatting for the UI.
enum Formatting {
    /// "resets in 2h 47m" / "resets in 40m" / "resetting now" / "" when unknown.
    static func resetCountdown(to date: Date?, now: Date) -> String {
        guard let date else { return "" }
        let remaining = Int(date.timeIntervalSince(now))
        if remaining <= 0 { return "resetting now" }
        let hours = remaining / 3600
        let minutes = (remaining % 3600) / 60
        if hours > 0 { return "resets in \(hours)h \(minutes)m" }
        return "resets in \(minutes)m"
    }

    /// "resets Mon, May 25" — absolute date, used for weekly windows.
    static func resetDate(_ date: Date?) -> String {
        guard let date else { return "" }
        let formatter = DateFormatter()
        formatter.dateFormat = "EEE, MMM d"
        return "resets \(formatter.string(from: date))"
    }

    /// "updated just now" / "updated 10s ago" / "updated 5m ago" / "updated 2h ago".
    static func updatedAgo(_ date: Date, now: Date) -> String {
        let elapsed = Int(now.timeIntervalSince(date))
        if elapsed < 3 { return "updated just now" }
        if elapsed < 60 { return "updated \(elapsed)s ago" }
        if elapsed < 3600 { return "updated \(elapsed / 60)m ago" }
        return "updated \(elapsed / 3600)h ago"
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter FormattingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/UI/Formatting.swift Tests/TokenSpendieTests/FormattingTests.swift
git commit -m "Add usage level and time formatting helpers"
```

---

## Task 11: UsageStore

The orchestrator: loads the cached snapshot, polls on a timer, runs the 401 re-read retry, publishes `snapshot` and `state`, and tracks staleness.

**Files:**
- Create: `Sources/TokenSpendie/Store/UsageStore.swift`
- Create: `Tests/TokenSpendieTests/UsageStoreTests.swift`

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import TokenSpendie

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
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter UsageStoreTests`
Expected: FAIL — `cannot find 'UsageStore' in scope`.

- [ ] **Step 3: Write `UsageStore.swift`**

```swift
import Foundation
import Combine
import Network

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
    private var panelVisible = false
    private var lastSuccess: Date?
    private let pathMonitor = NWPathMonitor()
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
            state = .error(.claudeCodeNotFound)
        } catch CredentialError.accessDenied {
            state = .error(.keychainAccessDenied)
        } catch {
            degrade(to: .badResponse)
        }
    }

    /// Call when the popover/floating panel opens or closes; tightens the poll interval while open.
    func setPanelVisible(_ visible: Bool) {
        guard visible != panelVisible else { return }
        panelVisible = visible
        rescheduleTimer()
        if visible { Task { await refreshNow() } }
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
        var wasSatisfied = true
        pathMonitor.pathUpdateHandler = { [weak self] path in
            let satisfied = path.status == .satisfied
            defer { wasSatisfied = satisfied }
            if satisfied && !wasSatisfied {
                Task { @MainActor in await self?.refreshNow() }
            }
        }
        pathMonitor.start(queue: DispatchQueue(label: "TokenSpendie.network"))
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter UsageStoreTests`
Expected: PASS.

- [ ] **Step 5: Run the full suite**

Run: `swift test`
Expected: all suites pass.

- [ ] **Step 6: Commit**

```bash
git add Sources/TokenSpendie/Store/UsageStore.swift Tests/TokenSpendieTests/UsageStoreTests.swift
git commit -m "Add UsageStore polling and orchestration"
```

---

## Task 12: RingView + menu bar label

The SwiftUI ring used in the menu bar, plus the compact menu bar label (ring + percentage / status glyph). No unit tests — verified by build and later runtime QA.

**Files:**
- Create: `Sources/TokenSpendie/UI/RingView.swift`

- [ ] **Step 1: Write `RingView.swift`**

```swift
import SwiftUI

/// A circular progress ring filled to `percent` (0–100), colored by usage level.
struct RingView: View {
    let percent: Double
    var lineWidth: CGFloat = 4
    var dimmed: Bool = false

    private var fraction: CGFloat { min(max(percent / 100, 0), 1) }
    private var color: Color {
        dimmed ? Color.secondary : UsageLevel.forPercent(percent).color
    }

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color.primary.opacity(0.18), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: fraction)
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
    }
}

/// The compact menu bar item: session ring + percentage, or a status glyph.
struct MenuBarLabel: View {
    @ObservedObject var store: UsageStore

    var body: some View {
        HStack(spacing: 4) {
            switch store.state {
            case .error(.claudeCodeNotFound):
                Text("✳ –").font(.system(size: 12, weight: .semibold))
            case .error:
                Text("✳ !").font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(UsageLevel.hot.color)
            case .loading where store.snapshot == nil:
                Text("✳ …").font(.system(size: 12, weight: .semibold))
            default:
                let percent = store.snapshot?.session.percent ?? 0
                RingView(percent: percent, lineWidth: 3, dimmed: store.state == .stale)
                    .frame(width: 14, height: 14)
                Text("\(Int(percent.rounded()))%")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(store.state == .stale ? Color.secondary : Color.primary)
            }
        }
        .padding(.horizontal, 2)
    }
}
```

- [ ] **Step 2: Build**

Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 3: Commit**

```bash
git add Sources/TokenSpendie/UI/RingView.swift
git commit -m "Add ring view and compact menu bar label"
```

---

## Task 13: DetailPanelView

The shared stacked-bar detail view used by the popover and the floating panel.

**Files:**
- Create: `Sources/TokenSpendie/UI/DetailPanelView.swift`

- [ ] **Step 1: Write `DetailPanelView.swift`**

```swift
import SwiftUI

/// One labelled progress bar with its reset line.
struct UsageBarRow: View {
    let title: String
    let subtitle: String
    let window: UsageWindow
    let resetLine: String
    var dimmed: Bool = false

    private var level: UsageLevel { UsageLevel.forPercent(window.percent) }
    private var fraction: CGFloat { min(max(window.percent / 100, 0), 1) }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Text(title).font(.system(size: 12, weight: .semibold))
                Spacer()
                Text("\(Int(window.percent.rounded()))%")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(dimmed ? Color.secondary : level.color)
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.primary.opacity(0.12))
                    Capsule()
                        .fill(dimmed ? Color.secondary : level.color)
                        .frame(width: geo.size.width * fraction)
                }
            }
            .frame(height: 7)
            Text(resetLine)
                .font(.system(size: 10))
                .foregroundStyle(Color.secondary)
        }
    }
}

/// The full detail panel: header, session/weekly/model rows, footer.
struct DetailPanelView: View {
    @ObservedObject var store: UsageStore
    var onRefresh: () -> Void
    var onOpenSettings: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Divider()
            content.padding(13)
            Divider()
            footer
        }
        .frame(width: 260)
    }

    private var header: some View {
        HStack {
            Text("TOKEN SPENDIE")
                .font(.system(size: 10, weight: .heavy)).kerning(0.5)
            Spacer()
            Button(action: onRefresh) {
                Image(systemName: "arrow.clockwise").font(.system(size: 11, weight: .semibold))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 13).padding(.vertical, 10)
    }

    @ViewBuilder
    private var content: some View {
        switch store.state {
        case .error(let kind):
            messageView(for: kind)
        case .loading where store.snapshot == nil:
            Text("Loading usage…").font(.system(size: 12)).foregroundStyle(.secondary)
        default:
            if let snapshot = store.snapshot {
                let dimmed = store.state == .stale
                VStack(alignment: .leading, spacing: 13) {
                    UsageBarRow(title: "Session", subtitle: "5-hour window",
                                window: snapshot.session,
                                resetLine: "5-hour window · " + Formatting.resetCountdown(to: snapshot.session.resetsAt, now: Date()),
                                dimmed: dimmed)
                    UsageBarRow(title: "Weekly", subtitle: "all models",
                                window: snapshot.weekly,
                                resetLine: "all models · " + Formatting.resetDate(snapshot.weekly.resetsAt),
                                dimmed: dimmed)
                    ForEach(snapshot.modelWeeklies, id: \.model) { item in
                        UsageBarRow(title: "Weekly · \(item.model)", subtitle: item.model,
                                    window: item.window,
                                    resetLine: "\(item.model) only · " + Formatting.resetDate(item.window.resetsAt),
                                    dimmed: dimmed)
                    }
                }
            }
        }
    }

    private func messageView(for kind: UsageError) -> some View {
        let (icon, text): (String, String) = {
            switch kind {
            case .claudeCodeNotFound:
                return ("🔌", "Claude Code not found. Install and log in to Claude Code, then this widget picks up your usage automatically.")
            case .keychainAccessDenied:
                return ("🔑", "Keychain access needed. The widget reads your Claude login token from the Keychain — click refresh and choose Allow.")
            case .loginExpired:
                return ("⏱", "Login expired. Run any Claude Code command to refresh your session — the widget recovers on its own after that.")
            case .network:
                return ("📡", "Can't reach the usage service. The widget will keep retrying.")
            case .badResponse:
                return ("⚠️", "Couldn't read usage data. The usage source returned something unexpected — the widget will keep retrying.")
            }
        }()
        return HStack(alignment: .top, spacing: 8) {
            Text(icon).font(.system(size: 15))
            Text(text).font(.system(size: 11)).foregroundStyle(.primary.opacity(0.85))
        }
    }

    private var footer: some View {
        HStack {
            Text(footerStatus).font(.system(size: 9)).foregroundStyle(.secondary)
            Spacer()
            Button(action: onOpenSettings) {
                Text("⚙ Settings").font(.system(size: 9))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 13).padding(.vertical, 8)
    }

    private var footerStatus: String {
        guard let snapshot = store.snapshot else { return " " }
        let ago = Formatting.updatedAgo(snapshot.fetchedAt, now: Date())
        return store.state == .stale ? "offline — \(ago)" : ago
    }
}
```

- [ ] **Step 2: Build**

Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 3: Commit**

```bash
git add Sources/TokenSpendie/UI/DetailPanelView.swift
git commit -m "Add detail panel view"
```

---

## Task 14: PreferencesView + launch-at-login

The settings UI and `SMAppService` launch-at-login integration.

**Files:**
- Create: `Sources/TokenSpendie/UI/PreferencesView.swift`

- [ ] **Step 1: Write `PreferencesView.swift`**

```swift
import SwiftUI
import ServiceManagement

/// Wraps `SMAppService` so launch-at-login can be toggled and queried.
enum LoginItem {
    static var isEnabled: Bool {
        SMAppService.mainApp.status == .enabled
    }

    /// Returns true on success. A failure (e.g. unsigned build restrictions) is reported back
    /// so the UI can revert the toggle.
    @discardableResult
    static func setEnabled(_ enabled: Bool) -> Bool {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            return true
        } catch {
            return false
        }
    }
}

struct PreferencesView: View {
    @ObservedObject var preferences: Preferences
    var onDisplayChanged: () -> Void
    var onIntervalChanged: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Token Spendie").font(.system(size: 14, weight: .bold))

            VStack(alignment: .leading, spacing: 8) {
                Text("DISPLAY").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                Toggle("Show menu bar item", isOn: $preferences.showMenuBar)
                    .onChange(of: preferences.showMenuBar) { _ in enforceAtLeastOneSurface(changed: .menuBar) }
                Toggle("Show floating panel", isOn: $preferences.showFloatingPanel)
                    .onChange(of: preferences.showFloatingPanel) { _ in enforceAtLeastOneSurface(changed: .floating) }
            }

            VStack(alignment: .leading, spacing: 8) {
                Text("REFRESH").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                Picker("Interval", selection: $preferences.refreshInterval) {
                    ForEach(RefreshInterval.allCases) { Text($0.label).tag($0) }
                }
                .onChange(of: preferences.refreshInterval) { _ in onIntervalChanged() }
            }

            Toggle("Launch at login", isOn: $preferences.launchAtLogin)
                .onChange(of: preferences.launchAtLogin) { newValue in
                    if !LoginItem.setEnabled(newValue) {
                        preferences.launchAtLogin = LoginItem.isEnabled   // revert on failure
                    }
                }

            HStack {
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
            }
        }
        .padding(20)
        .frame(width: 300)
        .onAppear { preferences.launchAtLogin = LoginItem.isEnabled }
    }

    private enum Surface { case menuBar, floating }

    /// At least one display surface must stay enabled; re-enable the other if both go off.
    private func enforceAtLeastOneSurface(changed: Surface) {
        if !preferences.showMenuBar && !preferences.showFloatingPanel {
            switch changed {
            case .menuBar: preferences.showFloatingPanel = true
            case .floating: preferences.showMenuBar = true
            }
        }
        onDisplayChanged()
    }
}
```

- [ ] **Step 2: Build**

Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 3: Commit**

```bash
git add Sources/TokenSpendie/UI/PreferencesView.swift
git commit -m "Add preferences view and launch-at-login"
```

---

## Task 15: MenuBarController

Owns the `NSStatusItem` (hosting `MenuBarLabel`) and a click-toggled `NSPopover` showing `DetailPanelView`.

**Files:**
- Create: `Sources/TokenSpendie/UI/MenuBarController.swift`

- [ ] **Step 1: Write `MenuBarController.swift`**

```swift
import AppKit
import SwiftUI

/// Manages the menu bar status item and its detail popover.
@MainActor
final class MenuBarController {
    private let store: UsageStore
    private let onOpenSettings: () -> Void
    private var statusItem: NSStatusItem?
    private let popover = NSPopover()

    init(store: UsageStore, onOpenSettings: @escaping () -> Void) {
        self.store = store
        self.onOpenSettings = onOpenSettings
    }

    /// Shows the status item. Safe to call repeatedly.
    func install() {
        guard statusItem == nil else { return }
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        guard let button = item.button else { return }

        let host = NSHostingView(rootView: MenuBarLabel(store: store))
        host.translatesAutoresizingMaskIntoConstraints = false
        button.addSubview(host)
        NSLayoutConstraint.activate([
            host.leadingAnchor.constraint(equalTo: button.leadingAnchor),
            host.trailingAnchor.constraint(equalTo: button.trailingAnchor),
            host.topAnchor.constraint(equalTo: button.topAnchor),
            host.bottomAnchor.constraint(equalTo: button.bottomAnchor),
        ])
        button.target = self
        button.action = #selector(togglePopover)

        popover.behavior = .transient
        popover.contentViewController = NSHostingController(
            rootView: DetailPanelView(
                store: store,
                onRefresh: { [weak self] in Task { await self?.store.refreshNow() } },
                onOpenSettings: { [weak self] in
                    self?.popover.performClose(nil)
                    self?.onOpenSettings()
                }
            )
        )
        self.statusItem = item
    }

    /// Removes the status item.
    func remove() {
        if let statusItem { NSStatusBar.system.removeStatusItem(statusItem) }
        statusItem = nil
    }

    @objc private func togglePopover() {
        guard let button = statusItem?.button else { return }
        if popover.isShown {
            popover.performClose(nil)
        } else {
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
            store.setPanelVisible(true)
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 3: Commit**

```bash
git add Sources/TokenSpendie/UI/MenuBarController.swift
git commit -m "Add menu bar controller"
```

---

## Task 16: FloatingPanelController

Owns an always-on-top, draggable `NSPanel` hosting `DetailPanelView`.

**Files:**
- Create: `Sources/TokenSpendie/UI/FloatingPanelController.swift`

- [ ] **Step 1: Write `FloatingPanelController.swift`**

```swift
import AppKit
import SwiftUI

/// Manages the optional always-on-top floating usage panel.
@MainActor
final class FloatingPanelController {
    private let store: UsageStore
    private let onOpenSettings: () -> Void
    private var panel: NSPanel?

    init(store: UsageStore, onOpenSettings: @escaping () -> Void) {
        self.store = store
        self.onOpenSettings = onOpenSettings
    }

    /// Shows the floating panel. Safe to call repeatedly.
    func show() {
        if let panel {
            panel.orderFrontRegardless()
            return
        }
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 260, height: 220),
            styleMask: [.nonactivatingPanel, .titled, .closable, .fullSizeContentView],
            backing: .buffered, defer: false
        )
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.isMovableByWindowBackground = true
        panel.level = .floating
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentViewController = NSHostingController(
            rootView: DetailPanelView(
                store: store,
                onRefresh: { [weak self] in Task { await self?.store.refreshNow() } },
                onOpenSettings: { [weak self] in self?.onOpenSettings() }
            )
        )
        panel.center()
        panel.orderFrontRegardless()
        self.panel = panel
        store.setPanelVisible(true)
    }

    /// Hides and releases the floating panel.
    func hide() {
        panel?.close()
        panel = nil
    }
}
```

- [ ] **Step 2: Build**

Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 3: Commit**

```bash
git add Sources/TokenSpendie/UI/FloatingPanelController.swift
git commit -m "Add floating panel controller"
```

---

## Task 17: AppDelegate wiring + end-to-end states

Connects every component, applies preferences, and provides the Settings window. After this the app runs end to end.

**Files:**
- Modify: `Sources/TokenSpendie/AppDelegate.swift`

- [ ] **Step 1: Replace `AppDelegate.swift`**

```swift
import AppKit
import SwiftUI
import Combine

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var preferences: Preferences!
    private var store: UsageStore!
    private var menuBar: MenuBarController!
    private var floatingPanel: FloatingPanelController!
    private var settingsWindow: NSWindow?
    private var cancellables = Set<AnyCancellable>()

    @MainActor
    func applicationDidFinishLaunching(_ notification: Notification) {
        preferences = Preferences()
        store = UsageStore(
            provider: EndpointUsageProvider(),
            credentials: KeychainReader(),
            cache: SnapshotCache(fileURL: SnapshotCache.defaultURL()),
            preferences: preferences
        )
        menuBar = MenuBarController(store: store, onOpenSettings: { [weak self] in self?.showSettings() })
        floatingPanel = FloatingPanelController(store: store, onOpenSettings: { [weak self] in self?.showSettings() })

        applyDisplayPreferences()
        store.start()

        // React to display-preference changes made outside PreferencesView (e.g. auto re-enable).
        preferences.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] in self?.applyDisplayPreferences() }
            .store(in: &cancellables)
    }

    /// Shows/hides each surface to match preferences.
    @MainActor
    private func applyDisplayPreferences() {
        if preferences.showMenuBar { menuBar.install() } else { menuBar.remove() }
        if preferences.showFloatingPanel { floatingPanel.show() } else { floatingPanel.hide() }
    }

    @MainActor
    private func showSettings() {
        if let settingsWindow {
            settingsWindow.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        let view = PreferencesView(
            preferences: preferences,
            onDisplayChanged: { [weak self] in self?.applyDisplayPreferences() },
            onIntervalChanged: { [weak self] in self?.store.rescheduleTimer() }
        )
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 300, height: 320),
            styleMask: [.titled, .closable], backing: .buffered, defer: false
        )
        window.title = "Token Spendie"
        window.contentViewController = NSHostingController(rootView: view)
        window.isReleasedWhenClosed = false
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow = window
    }
}
```

- [ ] **Step 2: Build and run**

Run: `swift build`
Expected: `Build complete!`
Run: `swift run TokenSpendie`
Expected: a ring + percentage appears in the menu bar within a few seconds. A Keychain prompt may appear — click **Allow**.

- [ ] **Step 3: Manual verification**

Confirm each:
1. The menu bar shows the session ring + percentage matching `claude` → `/usage`.
2. Clicking the menu bar item opens the detail popover with Session, Weekly, and any model rows.
3. The popover footer shows "updated Ns ago".
4. Open Settings (footer ⚙). Toggle "Show floating panel" on — the floating panel appears and stays above other windows; drag it by its body.
5. Toggle "Show menu bar item" off while the floating panel is on — the menu bar item disappears; the floating panel remains.
6. Change the refresh interval; the app keeps working.
7. Quit via the Settings "Quit" button.

Stop the run with Ctrl+C if still attached.

- [ ] **Step 4: Commit**

```bash
git add Sources/TokenSpendie/AppDelegate.swift
git commit -m "Wire app components together end to end"
```

---

## Task 18: Icon, Info.plist, build.sh, README + final QA

Packages the app into a shareable `TokenSpendie.app` and documents installation.

**Files:**
- Create: `Tools/makeicon.swift`
- Create: `Resources/Info.plist`
- Create: `build.sh`
- Create: `README.md`

- [ ] **Step 1: Write `Tools/makeicon.swift`**

```swift
// Tools/makeicon.swift — run with: swift Tools/makeicon.swift
// Draws Resources/AppIcon-1024.png (a usage ring on a rounded dark tile).
import AppKit

let size: CGFloat = 1024
let image = NSImage(size: NSSize(width: size, height: size))
image.lockFocus()

NSColor(calibratedRed: 0.12, green: 0.12, blue: 0.16, alpha: 1).setFill()
NSBezierPath(roundedRect: NSRect(x: 0, y: 0, width: size, height: size),
             xRadius: 180, yRadius: 180).fill()

let center = NSPoint(x: size / 2, y: size / 2)
let radius: CGFloat = 280
let track = NSBezierPath()
track.appendArc(withCenter: center, radius: radius, startAngle: 0, endAngle: 360)
track.lineWidth = 110
NSColor(white: 1, alpha: 0.16).setStroke()
track.stroke()

let progress = NSBezierPath()
progress.appendArc(withCenter: center, radius: radius, startAngle: 90, endAngle: 90 - 360 * 0.62, clockwise: true)
progress.lineWidth = 110
progress.lineCapStyle = .round
NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 1).setStroke()
progress.stroke()

image.unlockFocus()

guard let tiff = image.tiffRepresentation,
      let rep = NSBitmapImageRep(data: tiff),
      let png = rep.representation(using: .png, properties: [:]) else {
    FileHandle.standardError.write(Data("icon render failed\n".utf8))
    exit(1)
}
try! FileManager.default.createDirectory(atPath: "Resources", withIntermediateDirectories: true)
try! png.write(to: URL(fileURLWithPath: "Resources/AppIcon-1024.png"))
print("wrote Resources/AppIcon-1024.png")
```

- [ ] **Step 2: Write `Resources/Info.plist`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>            <string>TokenSpendie</string>
    <key>CFBundleDisplayName</key>     <string>Token Spendie</string>
    <key>CFBundleIdentifier</key>      <string>com.cherise.TokenSpendie</string>
    <key>CFBundleVersion</key>         <string>1</string>
    <key>CFBundleShortVersionString</key> <string>1.0.0</string>
    <key>CFBundlePackageType</key>     <string>APPL</string>
    <key>CFBundleExecutable</key>      <string>TokenSpendie</string>
    <key>CFBundleIconFile</key>        <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>  <string>13.0</string>
    <key>LSUIElement</key>             <true/>
    <key>NSHumanReadableCopyright</key><string>Personal use.</string>
</dict>
</plist>
```

- [ ] **Step 3: Write `build.sh`**

```bash
#!/usr/bin/env bash
# Builds TokenSpendie.app and a shareable zip.
set -euo pipefail
cd "$(dirname "$0")"

APP="build/TokenSpendie.app"
BIN_NAME="TokenSpendie"

echo "==> Compiling (release)"
swift build -c release

echo "==> Generating icon"
swift Tools/makeicon.swift
ICONSET="build/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
for s in 16 32 64 128 256 512; do
  sips -z $s $s     Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}.png"   >/dev/null
  sips -z $((s*2)) $((s*2)) Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o build/AppIcon.icns

echo "==> Assembling app bundle"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp ".build/release/$BIN_NAME" "$APP/Contents/MacOS/$BIN_NAME"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp build/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

echo "==> Zipping for sharing"
( cd build && rm -f TokenSpendie.zip && ditto -c -k --keepParent TokenSpendie.app TokenSpendie.zip )

echo "==> Done: $APP  and  build/TokenSpendie.zip"
```

- [ ] **Step 4: Write `README.md`**

```markdown
# Token Spendie

A macOS menu bar widget that shows your Claude Code usage — the 5-hour session
window and weekly caps — in real time.

## Build

Requires the Swift toolchain (Xcode Command Line Tools). No Xcode needed.

    ./build.sh

This produces `build/TokenSpendie.app` and `build/TokenSpendie.zip`.

## Install

1. Unzip `TokenSpendie.zip` and move `TokenSpendie.app` to `/Applications`.
2. **First launch:** right-click the app → **Open**, then confirm. This is
   required once because the app is not notarized.
3. When macOS asks for Keychain access, choose **Allow** — the widget reads your
   Claude Code login token to fetch usage.

## Requirements

- macOS 13 (Ventura) or later.
- Claude Code installed and logged in (`claude` working in a terminal). The
  widget reads the token Claude Code already stores; it never logs you in.

## Using it

- The menu bar shows your session usage as a ring. Click it for the full
  breakdown (session, weekly, per-model weekly).
- In Settings you can enable a floating always-on-top panel, change the refresh
  interval, and toggle launch-at-login.

## Sharing with friends

Send them `TokenSpendie.zip`. They follow the same Install steps. Each machine
uses its own Claude Code login automatically — there is nothing to configure.
```

- [ ] **Step 5: Make `build.sh` executable and run it**

Run: `chmod +x build.sh && ./build.sh`
Expected: ends with `Done: build/TokenSpendie.app  and  build/TokenSpendie.zip`.

- [ ] **Step 6: Final QA — launch the packaged app**

Run: `open build/TokenSpendie.app`
Confirm:
1. No Dock icon appears (it is an `LSUIElement` accessory app).
2. The menu bar ring appears and shows real usage.
3. The popover, floating panel, settings, and launch-at-login toggle all work as in Task 17.
4. Quit via Settings → Quit.

- [ ] **Step 7: Add build artifacts to `.gitignore` and commit**

```bash
printf 'build/\nResources/AppIcon-1024.png\n' >> .gitignore
git add Tools/makeicon.swift Resources/Info.plist build.sh README.md .gitignore
git commit -m "Add packaging, icon generation, and README"
```

---

## Self-Review

**Spec coverage** — every spec section maps to a task:

- Data source (Keychain + endpoint, swappable) → Tasks 1, 5, 7.
- Architecture components (`KeychainReader`, `UsageProvider`, `UsageSnapshot`, `UsageStore`, `MenuBarController`, `FloatingPanelController`, `PreferencesView`, `AppDelegate`) → Tasks 3–17.
- UI design (menu bar ring, stacked-bar detail panel, color scale, floating panel) → Tasks 10, 12, 13, 15, 16.
- Data flow & refresh (cache-first launch, 60 s poll, 20 s when panel open, manual/wake/network triggers, 401 re-read, staleness) → Tasks 8, 11.
- Error handling & six states → `UsageError` (Task 3), store mapping (Task 11), `DetailPanelView` messages + `MenuBarLabel` glyphs (Tasks 12, 13).
- Preferences (menu bar / floating / interval / launch-at-login) → Tasks 9, 14.
- Build, packaging, sharing → Task 18.
- Testing (pure-logic unit tests, protocol-mocked store) → Tasks 3–11.

**Deviations from spec (intentional, noted for review):**

1. The spec's design showed a single "Weekly · Opus" row. The Claude Code binary revealed the endpoint also returns `seven_day_sonnet`, so the model and `DetailPanelView` render model-specific weekly rows generically (Opus and/or Sonnet, whichever are present). This honors the spec's rule — "the model-specific row appears only if reported" — and costs nothing extra.
2. The spec's data layer included a header-based fallback provider (Approach 2). Task 1's probe proved the `anthropic-ratelimit-unified-*` headers are absent from `/api/oauth/usage`, so the concrete `HeaderUsageProvider` is dropped (Task 7). The `UsageProvider` protocol remains as the swappable seam the spec actually required, so a working fallback can still be added later without touching callers.

**Placeholder scan:** no `TBD`/`TODO`/"handle edge cases" — every step has complete code or an exact command.

**Type consistency:** `UsageWindow`, `ModelWeekly`, `UsageSnapshot`, `UsageError`, `ProviderError`, `LoadState` (Task 3); `OAuthCredentials`, `CredentialError`, `CredentialStore` (Task 4); `HTTPTransport`, `UsageProvider` (Task 7); `RefreshInterval`, `Preferences` (Task 9); `UsageLevel`, `Formatting` (Task 10) — all referenced consistently by later tasks. `UsageStore` init signature (Task 11) matches its construction in `AppDelegate` (Task 17).
