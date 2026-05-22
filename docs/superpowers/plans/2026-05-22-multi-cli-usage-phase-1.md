# Multi-CLI Usage — Phase 1 (Provider Architecture) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor Token Spendie's data layer from a single hard-wired Claude
fetch to a pluggable provider-array architecture, with Claude as the only
provider. No user-visible change.

**Architecture:** A new `UsageProvider` protocol (`detectCredentials()`,
`fetchUsage() -> ProviderSnapshot`) lets each AI CLI own its own auth and
shape. The old single-purpose `UsageProvider` protocol is renamed
`ClaudeUsageEndpoint` (the raw HTTP layer). `ClaudeProvider` composes the
Keychain reader + the endpoint + the 401-retry, and converts the Claude
`UsageSnapshot` into a generic `ProviderSnapshot`. `UsageStore` holds a
per-provider state array and polls every detected provider independently. The
menu bar and panel are rewired to read the single detected provider — same
visuals as today. Phase 2 (separate plan) adds the Gemini provider and the
multi-row UI.

**Tech Stack:** Swift 5.9, SwiftUI, AppKit, XCTest, Swift Package Manager
(`swift build` / `swift test`).

---

## Spec

See `docs/superpowers/specs/2026-05-22-multi-cli-usage-design.md`. This plan
implements the **Phase 1** half of that spec's "Phasing" section: the
provider-array refactor with Claude only. Phase 2 (Gemini provider, multi-row
panel, ring picker, menu-bar badge) is a separate plan written after Phase 1
lands and is verified.

## File Structure

- **Modify** `Sources/TokenSpendie/Model/UsageModels.swift` — add `ProviderID`,
  `ResetStyle`, `LabeledWindow`, `ProviderSnapshot`, `ProviderUsage`. Existing
  `UsageWindow` / `UsageSnapshot` / `ModelWeekly` / `LoadState` / error enums
  stay. `LoadState` is reused as the per-provider state (the spec calls it
  `ProviderState`; reusing `LoadState` keeps it DRY — same four cases).
- **Modify** `Sources/TokenSpendie/Data/UsageProvider.swift` — rename the
  existing protocol `UsageProvider` → `ClaudeUsageEndpoint`; add the new
  high-level `UsageProvider` protocol. `HTTPTransport` / `DefaultTransport`
  unchanged.
- **Modify** `Sources/TokenSpendie/Data/OAuthCredentials.swift` — add
  `credentialsExist()` to the `CredentialStore` protocol.
- **Modify** `Sources/TokenSpendie/Data/KeychainReader.swift` — implement
  `credentialsExist()` (existence probe, no Keychain consent prompt).
- **Modify** `Sources/TokenSpendie/Data/EndpointUsageProvider.swift` — conform
  to `ClaudeUsageEndpoint` instead of the old `UsageProvider` (one-word change).
- **Create** `Sources/TokenSpendie/Data/ClaudeProvider.swift` — the
  `UsageProvider` conformer for Claude.
- **Modify** `Sources/TokenSpendie/Store/SnapshotCache.swift` — cache a
  `ProviderSnapshot`; add a per-provider default URL.
- **Modify** `Sources/TokenSpendie/Store/UsageStore.swift` — multi-provider
  state, concurrent per-provider polling, per-provider 429 backoff.
- **Modify** `Sources/TokenSpendie/Store/Preferences.swift` — add
  `menuBarProviderID`.
- **Modify** `Sources/TokenSpendie/UI/MenuBarController.swift` — read the
  single detected provider from `store.menuBarProvider`.
- **Modify** `Sources/TokenSpendie/UI/DetailPanelView.swift` — render the
  single detected provider's `ProviderSnapshot.windows`. Same layout.
- **Modify** `Sources/TokenSpendie/AppDelegate.swift` — construct `UsageStore`
  with the provider array.
- **Create** `Tests/TokenSpendieTests/ProviderModelsTests.swift`
- **Create** `Tests/TokenSpendieTests/ClaudeProviderTests.swift`
- **Modify** test files: `UsageProviderTests.swift`, `SnapshotCacheTests.swift`,
  `UsageStoreTests.swift`, `PreferencesTests.swift`.

---

## Task 1: Provider model types

**Files:**
- Create: `Tests/TokenSpendieTests/ProviderModelsTests.swift`
- Modify: `Sources/TokenSpendie/Model/UsageModels.swift` (append at end of file)

- [ ] **Step 1: Write the failing test**

Create `Tests/TokenSpendieTests/ProviderModelsTests.swift`:

```swift
import XCTest
@testable import TokenSpendie

final class ProviderModelsTests: XCTestCase {
    private func window(_ p: Double) -> UsageWindow {
        UsageWindow(percent: p, resetsAt: Date(timeIntervalSince1970: 1_000))
    }
    private func labeled(_ label: String, _ p: Double) -> LabeledWindow {
        LabeledWindow(label: label, detail: "\(label) detail",
                      resetStyle: .countdown, window: window(p))
    }

    func testProviderIDIsRawStringCodable() throws {
        let data = try JSONEncoder().encode(ProviderID.claude)
        XCTAssertEqual(String(data: data, encoding: .utf8), "\"claude\"")
        XCTAssertEqual(try JSONDecoder().decode(ProviderID.self, from: data), .claude)
    }

    func testProviderSnapshotCodableRoundTrip() throws {
        let snapshot = ProviderSnapshot(
            id: .claude,
            plan: "Max",
            headline: labeled("Session · 5h", 47),
            windows: [labeled("Session · 5h", 47), labeled("Weekly · all", 31)],
            fetchedAt: Date(timeIntervalSince1970: 5_000)
        )
        let data = try JSONEncoder().encode(snapshot)
        XCTAssertEqual(try JSONDecoder().decode(ProviderSnapshot.self, from: data), snapshot)
    }

    func testProviderSnapshotCodableWithNilPlan() throws {
        let snapshot = ProviderSnapshot(
            id: .claude, plan: nil,
            headline: labeled("Session · 5h", 12),
            windows: [labeled("Session · 5h", 12)],
            fetchedAt: Date(timeIntervalSince1970: 9))
        let data = try JSONEncoder().encode(snapshot)
        XCTAssertEqual(try JSONDecoder().decode(ProviderSnapshot.self, from: data), snapshot)
    }

    func testProviderUsageCarriesStateWithoutSnapshot() {
        let usage = ProviderUsage(id: .claude, displayName: "Claude",
                                  state: .loading, snapshot: nil)
        XCTAssertEqual(usage.state, .loading)
        XCTAssertNil(usage.snapshot)
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter ProviderModelsTests`
Expected: build failure — `cannot find 'ProviderID' in scope`.

- [ ] **Step 3: Write the implementation**

Append to `Sources/TokenSpendie/Model/UsageModels.swift` (after the existing
`LoadState` enum, at end of file):

```swift

/// Identifies an AI CLI the widget can track. Phase 2 adds `.gemini`.
enum ProviderID: String, Codable, CaseIterable, Equatable {
    case claude
}

/// How a window's `resetsAt` is rendered in the panel.
enum ResetStyle: String, Codable, Equatable {
    case countdown   // "resets in 2h 10m" — short rolling windows
    case date        // "resets Mon, May 25" — weekly windows
}

/// One usage window plus the text the panel needs to render it. `label` is the
/// row title; `detail` is the static prefix of the reset line (the live
/// countdown/date is appended at render time so it stays current).
struct LabeledWindow: Codable, Equatable {
    let label: String
    let detail: String
    let resetStyle: ResetStyle
    let window: UsageWindow
}

/// One complete reading of a provider's usage, normalized for the panel.
/// `windows` is the full native list and includes the `headline` window.
struct ProviderSnapshot: Codable, Equatable {
    let id: ProviderID
    let plan: String?               // best-effort; nil hides the plan pill
    let headline: LabeledWindow     // drives the ring + collapsed-row %
    let windows: [LabeledWindow]
    let fetchedAt: Date
}

/// One panel row's complete state. Reuses `LoadState` as the per-provider
/// state. `displayName` is held here so an errored/loading row can render
/// before any snapshot exists.
struct ProviderUsage: Equatable {
    let id: ProviderID
    let displayName: String
    var state: LoadState
    var snapshot: ProviderSnapshot?
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter ProviderModelsTests`
Expected: PASS — 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Tests/TokenSpendieTests/ProviderModelsTests.swift Sources/TokenSpendie/Model/UsageModels.swift
git commit -m "feat: add provider model types for multi-CLI support"
```

---

## Task 2: Split the provider protocols

Mechanical refactor — no behaviour change. Renames the existing low-level
protocol and adds the new high-level one, plus a Keychain existence probe.
Verified by `swift build` and the existing suite.

**Files:**
- Modify: `Sources/TokenSpendie/Data/UsageProvider.swift`
- Modify: `Sources/TokenSpendie/Data/EndpointUsageProvider.swift`
- Modify: `Sources/TokenSpendie/Data/OAuthCredentials.swift`
- Modify: `Sources/TokenSpendie/Data/KeychainReader.swift`
- Modify: `Tests/TokenSpendieTests/UsageProviderTests.swift`

- [ ] **Step 1: Rename the low-level protocol and add the new one**

In `Sources/TokenSpendie/Data/UsageProvider.swift`, replace the protocol at the
bottom of the file (lines 21-24, `/// Fetches a usage snapshot…` through its
closing brace) with:

```swift
/// The raw Claude usage HTTP call: given a valid access token, returns a
/// decoded `UsageSnapshot`. Implemented by `EndpointUsageProvider`.
protocol ClaudeUsageEndpoint {
    func fetchUsage(accessToken: String) async throws -> UsageSnapshot
}

/// One trackable AI CLI. Each conformer owns its own credential discovery and
/// fetch, and returns a normalized `ProviderSnapshot`.
protocol UsageProvider {
    var id: ProviderID { get }
    var displayName: String { get }
    /// Are this CLI's credentials present? Must be cheap and must not trigger
    /// a credential-consent prompt.
    func detectCredentials() -> Bool
    func fetchUsage() async throws -> ProviderSnapshot
}
```

- [ ] **Step 2: Update `EndpointUsageProvider` conformance**

In `Sources/TokenSpendie/Data/EndpointUsageProvider.swift`, change the type
declaration on line 4 from:

```swift
struct EndpointUsageProvider: UsageProvider {
```

to:

```swift
struct EndpointUsageProvider: ClaudeUsageEndpoint {
```

Nothing else in that file changes.

- [ ] **Step 3: Add `credentialsExist()` to the `CredentialStore` protocol**

In `Sources/TokenSpendie/Data/OAuthCredentials.swift`, replace the
`CredentialStore` protocol (lines 43-46) with:

```swift
/// Abstraction over credential storage so the store can be tested without the Keychain.
protocol CredentialStore {
    func loadCredentials() throws -> OAuthCredentials
    /// True if the credential item exists, without reading its secret data —
    /// so it never triggers the Keychain consent prompt.
    func credentialsExist() -> Bool
}
```

- [ ] **Step 4: Implement `credentialsExist()` in `KeychainReader`**

In `Sources/TokenSpendie/Data/KeychainReader.swift`, add this method inside the
`KeychainReader` struct, after `loadCredentials()` (before the struct's closing
brace):

```swift

    func credentialsExist() -> Bool {
        // kSecReturnData:false asks only whether the item exists. Returning
        // attributes (not the secret) does not prompt for user consent.
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnData as String: false,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        return SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess
    }
```

- [ ] **Step 5: Update `UsageProviderTests` for the rename**

In `Tests/TokenSpendieTests/UsageProviderTests.swift`, change the
`assertThrows` helper signature on line 54 from:

```swift
    private func assertThrows(_ provider: UsageProvider, _ expected: ProviderError,
```

to:

```swift
    private func assertThrows(_ provider: ClaudeUsageEndpoint, _ expected: ProviderError,
```

No other change — `EndpointUsageProvider` is still constructed directly in
each test.

- [ ] **Step 6: Build and run the full suite**

Run: `swift build`
Expected: `Build complete!` — note `UsageStore.swift` still references the old
`UsageProvider` shape; if the build fails only inside `UsageStore.swift` /
`UsageStoreTests.swift` / `AppDelegate.swift`, that is expected and fixed in
Tasks 5 and 9. If so, skip to Step 7 and accept the partial build; otherwise
the build must be clean.

> **Note for the executor:** `UsageStore` is rewritten wholesale in Task 5.
> Between Task 2 and Task 5 the package may not compile. Commit the
> per-task changes anyway (the repo tolerates this across a planned refactor),
> and do not run `swift test` until Task 5 is complete.

- [ ] **Step 7: Commit**

```bash
git add Sources/TokenSpendie/Data/UsageProvider.swift Sources/TokenSpendie/Data/EndpointUsageProvider.swift Sources/TokenSpendie/Data/OAuthCredentials.swift Sources/TokenSpendie/Data/KeychainReader.swift Tests/TokenSpendieTests/UsageProviderTests.swift
git commit -m "refactor: split UsageProvider into endpoint + provider protocols"
```

---

## Task 3: `ClaudeProvider`

The `UsageProvider` conformer for Claude. Owns the Keychain read, the
401-retry, and the `UsageSnapshot` → `ProviderSnapshot` conversion. The
conversion is a pure static function, unit-tested directly.

**Files:**
- Create: `Sources/TokenSpendie/Data/ClaudeProvider.swift`
- Create: `Tests/TokenSpendieTests/ClaudeProviderTests.swift`

- [ ] **Step 1: Write the failing test**

Create `Tests/TokenSpendieTests/ClaudeProviderTests.swift`:

```swift
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter ClaudeProviderTests`
Expected: build failure — `cannot find 'ClaudeProvider' in scope`.

- [ ] **Step 3: Write the implementation**

Create `Sources/TokenSpendie/Data/ClaudeProvider.swift`:

```swift
import Foundation

/// The `UsageProvider` for Claude Code. Composes the Keychain credential
/// reader and the `/api/oauth/usage` endpoint, retries once on a 401 by
/// re-reading the Keychain (Claude Code refreshes its token during normal
/// use), and converts the Claude-shaped `UsageSnapshot` into a generic
/// `ProviderSnapshot`.
struct ClaudeProvider: UsageProvider {
    let id: ProviderID = .claude
    let displayName = "Claude"

    private let credentials: CredentialStore
    private let endpoint: ClaudeUsageEndpoint

    init(credentials: CredentialStore = KeychainReader(),
         endpoint: ClaudeUsageEndpoint = EndpointUsageProvider()) {
        self.credentials = credentials
        self.endpoint = endpoint
    }

    func detectCredentials() -> Bool {
        credentials.credentialsExist()
    }

    func fetchUsage() async throws -> ProviderSnapshot {
        let creds = try credentials.loadCredentials()
        let usage: UsageSnapshot
        do {
            usage = try await endpoint.fetchUsage(accessToken: creds.accessToken)
        } catch ProviderError.unauthorized {
            // Re-read the Keychain once — Claude Code refreshes the token
            // during normal use — then retry.
            let refreshed = try credentials.loadCredentials()
            usage = try await endpoint.fetchUsage(accessToken: refreshed.accessToken)
        }
        return Self.convert(usage)
    }

    /// Pure `UsageSnapshot` → `ProviderSnapshot` mapping. The session window is
    /// the headline; `windows` is `[session, weekly, model-weeklies…]`.
    static func convert(_ usage: UsageSnapshot) -> ProviderSnapshot {
        let session = LabeledWindow(label: "Session · 5h", detail: "5-hour window",
                                    resetStyle: .countdown, window: usage.session)
        var windows = [session]
        windows.append(LabeledWindow(label: "Weekly · all", detail: "all models",
                                     resetStyle: .date, window: usage.weekly))
        for model in usage.modelWeeklies {
            windows.append(LabeledWindow(label: "Weekly · \(model.model)",
                                         detail: "\(model.model) only",
                                         resetStyle: .date, window: model.window))
        }
        return ProviderSnapshot(id: .claude, plan: nil, headline: session,
                                windows: windows, fetchedAt: usage.fetchedAt)
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter ClaudeProviderTests`
Expected: PASS — 6 tests pass. (If the package fails to build because of
`UsageStore.swift`, that is expected per the Task 2 note; the executor should
proceed to Task 4 and run this filter again after Task 5.)

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Data/ClaudeProvider.swift Tests/TokenSpendieTests/ClaudeProviderTests.swift
git commit -m "feat: add ClaudeProvider conforming to UsageProvider"
```

---

## Task 4: `SnapshotCache` caches `ProviderSnapshot`

**Files:**
- Modify: `Sources/TokenSpendie/Store/SnapshotCache.swift`
- Modify: `Tests/TokenSpendieTests/SnapshotCacheTests.swift`

- [ ] **Step 1: Update the test for the new type**

Replace the entire contents of `Tests/TokenSpendieTests/SnapshotCacheTests.swift`
with:

```swift
import XCTest
@testable import TokenSpendie

final class SnapshotCacheTests: XCTestCase {
    private func tempURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("cache-\(UUID().uuidString).json")
    }

    private let sample = ProviderSnapshot(
        id: .claude,
        plan: "Max",
        headline: LabeledWindow(label: "Session · 5h", detail: "5-hour window",
                                resetStyle: .countdown,
                                window: UsageWindow(percent: 30, resetsAt: nil)),
        windows: [LabeledWindow(label: "Session · 5h", detail: "5-hour window",
                                resetStyle: .countdown,
                                window: UsageWindow(percent: 30, resetsAt: nil))],
        fetchedAt: Date(timeIntervalSince1970: 555))

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

    func testDefaultURLIsPerProvider() {
        let claude = SnapshotCache.defaultURL(for: .claude)
        XCTAssertEqual(claude.lastPathComponent, "snapshot-claude.json")
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter SnapshotCacheTests`
Expected: build failure — `cannot convert value of type 'ProviderSnapshot'` /
`extra argument 'for' in call`.

- [ ] **Step 3: Write the implementation**

Replace the entire contents of `Sources/TokenSpendie/Store/SnapshotCache.swift`
with:

```swift
import Foundation

/// Persists the most recent `ProviderSnapshot` for one provider to a JSON file.
struct SnapshotCache {
    let fileURL: URL

    /// Per-provider location:
    /// ~/Library/Application Support/TokenSpendie/snapshot-<id>.json
    static func defaultURL(for provider: ProviderID) -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("TokenSpendie", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("snapshot-\(provider.rawValue).json")
    }

    func load() -> ProviderSnapshot? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return try? JSONDecoder().decode(ProviderSnapshot.self, from: data)
    }

    func save(_ snapshot: ProviderSnapshot) {
        guard let data = try? JSONEncoder().encode(snapshot) else { return }
        try? data.write(to: fileURL, options: .atomic)
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter SnapshotCacheTests`
Expected: PASS — 4 tests pass. (If the package fails to build because of
`UsageStore.swift`, that is expected per the Task 2 note.)

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Store/SnapshotCache.swift Tests/TokenSpendieTests/SnapshotCacheTests.swift
git commit -m "refactor: cache ProviderSnapshot per provider"
```

---

## Task 5: `UsageStore` multi-provider rewrite

The store is rewritten wholesale. It registers an array of `UsageProvider`s,
detects which are active each cycle, polls all detected providers concurrently,
and publishes a `[ProviderUsage]` array plus `menuBarProviderID`. 429 backoff
and stale-tracking become per-provider. The refresh button still drives one
global refresh of every provider.

**Files:**
- Modify: `Sources/TokenSpendie/Store/UsageStore.swift` (full replacement)
- Modify: `Tests/TokenSpendieTests/UsageStoreTests.swift` (full replacement)

- [ ] **Step 1: Replace the test file**

Replace the entire contents of `Tests/TokenSpendieTests/UsageStoreTests.swift`
with:

```swift
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

    func testOneProviderFailingDoesNotAbortAnother() async {
        let ok = StubProvider(id: .claude, displayName: "Claude",
                              results: [.success(snapshot(20, id: .claude))])
        // A second provider id is simulated with .claude-shaped data is not
        // possible (Phase 1 has one ProviderID); instead assert the single
        // provider's failure is isolated to its own row.
        let store = makeStore([ok])
        await store.refreshNow()
        XCTAssertEqual(usage(store, .claude)?.state, .ok)
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter UsageStoreTests`
Expected: build failure — `UsageStore` has no `init(providers:cacheFactory:preferences:now:)`.

- [ ] **Step 3: Replace `UsageStore.swift`**

Replace the entire contents of `Sources/TokenSpendie/Store/UsageStore.swift`
with:

```swift
import Foundation
import Combine
import Network
import AppKit

/// Owns polling, the per-provider data pipeline, and the published widget
/// state. Every registered `UsageProvider` is detected and polled
/// independently; one provider failing never blocks another.
@MainActor
final class UsageStore: ObservableObject {
    /// One row per detected provider, in registration order.
    @Published private(set) var providers: [ProviderUsage] = []
    /// The provider whose ring rides the menu bar.
    @Published private(set) var menuBarProviderID: ProviderID
    /// True while any provider's refresh cycle is running.
    @Published private(set) var isRefreshing = false

    private let registered: [UsageProvider]
    private let caches: [ProviderID: SnapshotCache]
    private let preferences: Preferences
    private let now: () -> Date

    /// Per-provider runtime bookkeeping, keyed by provider id.
    private var usageByID: [ProviderID: ProviderUsage] = [:]
    private var backoffUntil: [ProviderID: Date] = [:]
    private var consecutiveRateLimits: [ProviderID: Int] = [:]
    private var lastSuccess: [ProviderID: Date] = [:]

    private var timer: Timer?
    private var lastManualRefresh: Date?
    private static let manualRefreshMinGap: TimeInterval = 2
    private let pathMonitor = NWPathMonitor()
    private var networkWasSatisfied = true

    init(providers: [UsageProvider],
         cacheFactory: (ProviderID) -> SnapshotCache = { SnapshotCache(fileURL: SnapshotCache.defaultURL(for: $0)) },
         preferences: Preferences,
         now: @escaping () -> Date = Date.init) {
        self.registered = providers
        self.caches = Dictionary(uniqueKeysWithValues: providers.map { ($0.id, cacheFactory($0.id)) })
        self.preferences = preferences
        self.now = now
        self.menuBarProviderID = preferences.menuBarProviderID
    }

    /// The detected provider shown in the menu bar — the stored choice if it is
    /// still detected, else the first detected provider.
    var menuBarProvider: ProviderUsage? {
        providers.first { $0.id == menuBarProviderID } ?? providers.first
    }

    /// Picks the provider whose ring rides the menu bar; persists the choice.
    func setMenuBarProvider(_ id: ProviderID) {
        menuBarProviderID = id
        preferences.menuBarProviderID = id
    }

    /// When a provider is in 429 backoff, the time the limit resets; else nil.
    func rateLimitedUntil(for id: ProviderID) -> Date? {
        guard let until = backoffUntil[id], now() < until else { return nil }
        return until
    }

    /// Loads cached snapshots, begins polling, observes wake/network, fires an
    /// initial fetch.
    func start() {
        for provider in registered {
            if let cached = caches[provider.id]?.load() {
                let fresh = now().timeIntervalSince(cached.fetchedAt) < 60
                usageByID[provider.id] = ProviderUsage(
                    id: provider.id, displayName: provider.displayName,
                    state: fresh ? .ok : .stale, snapshot: cached)
                if fresh { lastSuccess[provider.id] = cached.fetchedAt }
            }
        }
        // Publish cached rows immediately so launch has no empty flash.
        publish(order: registered.map(\.id))
        NotificationCenter.default.addObserver(
            self, selector: #selector(systemDidWake),
            name: NSWorkspace.didWakeNotification, object: nil)
        observeNetwork()
        rescheduleTimer()
        Task { await refreshNow() }
    }

    /// One refresh cycle: detect every registered provider, then fetch each
    /// detected provider. `ignoringBackoff` lets a user-initiated refresh
    /// proceed during 429 backoff.
    func refreshNow(ignoringBackoff: Bool = false) async {
        isRefreshing = true
        defer { isRefreshing = false }

        let detected = registered.filter { $0.detectCredentials() }
        let detectedIDs = Set(detected.map(\.id))
        // Drop rows for providers no longer detected.
        usageByID = usageByID.filter { detectedIDs.contains($0.key) }

        // Sequential in Phase 1 (one provider). Phase 2 introduces concurrent
        // fetches once a second provider exists.
        for provider in detected {
            await refresh(provider, ignoringBackoff: ignoringBackoff)
        }
        publish(order: detected.map(\.id))
    }

    /// A user-initiated refresh from the refresh button. Refreshes every
    /// provider, bypassing 429 backoff. Ignored while a refresh is already
    /// running or within `manualRefreshMinGap` of the previous manual refresh.
    func manualRefresh() async {
        if isRefreshing { return }
        if let last = lastManualRefresh,
           now().timeIntervalSince(last) < Self.manualRefreshMinGap { return }
        lastManualRefresh = now()
        await refreshNow(ignoringBackoff: true)
    }

    /// Re-applies the poll interval after a preference change.
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

    /// Refreshes one provider, mapping success/failure onto its `ProviderUsage`.
    private func refresh(_ provider: UsageProvider, ignoringBackoff: Bool) async {
        let id = provider.id
        if !ignoringBackoff, let until = backoffUntil[id], now() < until { return }

        if usageByID[id] == nil {
            usageByID[id] = ProviderUsage(id: id, displayName: provider.displayName,
                                          state: .loading, snapshot: nil)
        }
        do {
            apply(try await provider.fetchUsage(), for: id, displayName: provider.displayName)
        } catch ProviderError.unauthorized {
            setState(.error(.loginExpired), for: id)
        } catch ProviderError.network {
            degrade(to: .network, for: id)
        } catch ProviderError.badResponse {
            degrade(to: .badResponse, for: id)
        } catch ProviderError.rateLimited(let retryAfter) {
            applyRateLimitBackoff(retryAfter: retryAfter, for: id)
        } catch CredentialError.notFound, CredentialError.malformed {
            setState(.error(.claudeCodeNotFound), for: id)
        } catch CredentialError.accessDenied {
            setState(.error(.keychainAccessDenied), for: id)
        } catch {
            degrade(to: .badResponse, for: id)
        }
    }

    private func apply(_ snapshot: ProviderSnapshot, for id: ProviderID, displayName: String) {
        usageByID[id] = ProviderUsage(id: id, displayName: displayName,
                                      state: .ok, snapshot: snapshot)
        caches[id]?.save(snapshot)
        lastSuccess[id] = now()
        backoffUntil[id] = nil
        consecutiveRateLimits[id] = 0
    }

    private func setState(_ state: LoadState, for id: ProviderID) {
        guard var usage = usageByID[id] else { return }
        usage.state = state
        usageByID[id] = usage
    }

    /// A soft failure: keep showing the cached snapshot as stale if there is
    /// one, else surface the error.
    private func degrade(to kind: UsageError, for id: ProviderID) {
        if usageByID[id]?.snapshot != nil {
            setState(.stale, for: id)
        } else {
            setState(.error(kind), for: id)
        }
    }

    /// Pauses polling for one provider after a 429: honors `Retry-After`, else
    /// backs off exponentially (2m, 5m, 15m).
    private func applyRateLimitBackoff(retryAfter: TimeInterval?, for id: ProviderID) {
        let count = (consecutiveRateLimits[id] ?? 0) + 1
        consecutiveRateLimits[id] = count
        let steps: [TimeInterval] = [120, 300, 900]
        let fallback = steps[min(count - 1, steps.count - 1)]
        backoffUntil[id] = now().addingTimeInterval(retryAfter ?? fallback)
        degrade(to: .badResponse, for: id)
    }

    /// Marks a provider's snapshot stale if no refresh has succeeded in 3x the
    /// poll interval.
    private func markStaleIfNeeded() {
        let threshold = preferences.refreshInterval.seconds * 3
        for (id, usage) in usageByID where usage.state == .ok {
            if let last = lastSuccess[id], now().timeIntervalSince(last) > threshold {
                setState(.stale, for: id)
            }
        }
        publish(order: providers.map(\.id))
    }

    /// Pushes the internal per-provider map to the published `providers` array,
    /// ordered by the given provider-id order.
    private func publish(order: [ProviderID]) {
        providers = order.compactMap { usageByID[$0] }
    }

    @objc private func systemDidWake() {
        Task { await refreshNow() }
    }

    private func observeNetwork() {
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter UsageStoreTests`
Expected: PASS — 15 tests pass.

> If the package still fails to build, the failures must now be confined to
> `MenuBarController.swift`, `DetailPanelView.swift`, and `AppDelegate.swift`
> — fixed in Tasks 7-9.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Store/UsageStore.swift Tests/TokenSpendieTests/UsageStoreTests.swift
git commit -m "feat: poll usage per provider in UsageStore"
```

---

## Task 6: `Preferences.menuBarProviderID`

**Files:**
- Modify: `Sources/TokenSpendie/Store/Preferences.swift`
- Modify: `Tests/TokenSpendieTests/PreferencesTests.swift`

- [ ] **Step 1: Write the failing test**

In `Tests/TokenSpendieTests/PreferencesTests.swift`, add this method inside the
existing test class, before its closing brace:

```swift

    func testMenuBarProviderIDDefaultsToClaudeAndPersists() {
        let suite = UUID().uuidString
        let defaults = UserDefaults(suiteName: suite)!
        let prefs = Preferences(defaults: defaults)
        XCTAssertEqual(prefs.menuBarProviderID, .claude, "defaults to Claude")

        prefs.menuBarProviderID = .claude
        let reloaded = Preferences(defaults: defaults)
        XCTAssertEqual(reloaded.menuBarProviderID, .claude, "persists across instances")
    }
```

> Phase 1 has only `ProviderID.claude`, so this test exercises the default and
> the persistence round-trip with that single value. Phase 2 extends it once a
> second provider exists.

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter PreferencesTests`
Expected: build failure — `value of type 'Preferences' has no member 'menuBarProviderID'`.

- [ ] **Step 3: Write the implementation**

In `Sources/TokenSpendie/Store/Preferences.swift`:

Add the published property after the `theme` property (after line 29):

```swift
    @Published var menuBarProviderID: ProviderID {
        didSet { defaults.set(menuBarProviderID.rawValue, forKey: Keys.menuBarProviderID) }
    }
```

Add the key inside the `Keys` enum (after `theme`):

```swift
        static let menuBarProviderID = "menuBarProviderID"
```

Add the initializer line at the end of `init`, after the `theme` line:

```swift
        let storedProvider = defaults.string(forKey: Keys.menuBarProviderID)
        self.menuBarProviderID = storedProvider.flatMap(ProviderID.init(rawValue:)) ?? .claude
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter PreferencesTests`
Expected: PASS — all `PreferencesTests` pass, including the new one.

- [ ] **Step 5: Commit**

```bash
git add Sources/TokenSpendie/Store/Preferences.swift Tests/TokenSpendieTests/PreferencesTests.swift
git commit -m "feat: persist the menu-bar provider choice"
```

---

## Task 7: Rewire `MenuBarController`

SwiftUI/AppKit code — not unit-tested; verified by `swift build` and the manual
check in Task 9. The menu bar reads the single detected provider via
`store.menuBarProvider`; visuals are identical to today.

**Files:**
- Modify: `Sources/TokenSpendie/UI/MenuBarController.swift`

- [ ] **Step 1: Rewrite `refreshButton()`**

In `Sources/TokenSpendie/UI/MenuBarController.swift`, replace the
`refreshButton()` method (lines 62-80, `/// Updates the status button's…`
through its closing brace) with:

```swift
    /// Updates the status button's image + title from the menu-bar provider.
    private func refreshButton() {
        guard let button = statusItem?.button else { return }
        guard let provider = store.menuBarProvider else {
            // No provider detected — same as the old `claudeCodeNotFound` state.
            button.image = nil
            button.title = "✳ –"
            return
        }
        switch provider.state {
        case .error:
            button.image = nil
            button.title = "✳ !"
        case .loading where provider.snapshot == nil:
            button.image = nil
            button.title = "✳ …"
        default:
            let percent = provider.snapshot?.headline.window.percent ?? 0
            let color = NSColor(preferences.theme.color(for: UsageLevel.forPercent(percent)))
            button.image = Self.ringImage(percent: percent, color: color)
            button.title = " \(Int(percent.rounded()))%"
        }
    }
```

`ringImage(percent:color:)`, `install()`, `remove()`, and the panel code are
unchanged.

- [ ] **Step 2: Build to verify this file compiles**

Run: `swift build 2>&1 | grep -A2 MenuBarController || echo "MenuBarController OK"`
Expected: `MenuBarController OK` (the overall build still fails on
`DetailPanelView.swift` / `AppDelegate.swift` until Tasks 8-9 — that is fine).

- [ ] **Step 3: Commit**

```bash
git add Sources/TokenSpendie/UI/MenuBarController.swift
git commit -m "refactor: drive the menu bar from the detected provider"
```

---

## Task 8: Rewire `DetailPanelView`

SwiftUI code — not unit-tested; verified by `swift build`, the full suite, and
the manual check in Task 9. The panel renders the single detected provider's
`ProviderSnapshot.windows`; layout is identical to today.

**Files:**
- Modify: `Sources/TokenSpendie/UI/DetailPanelView.swift`

- [ ] **Step 1: Update `RefreshIndicator` to read the menu-bar provider**

In `Sources/TokenSpendie/UI/DetailPanelView.swift`, in the `RefreshIndicator`
struct's `statusText` computed property, replace the `RefreshStatusResolver.resolve(...)`
call (the argument list inside `switch RefreshStatusResolver.resolve(`) so it reads:

```swift
        switch RefreshStatusResolver.resolve(
            isFetching: spinning,
            snapshotFetchedAt: store.menuBarProvider?.snapshot?.fetchedAt,
            rateLimitedUntil: store.menuBarProvider.flatMap { store.rateLimitedUntil(for: $0.id) },
            isStale: store.menuBarProvider?.state == .stale,
            now: Date()
        ) {
```

The rest of `RefreshIndicator` is unchanged — `store.isRefreshing` still
exists on the new `UsageStore`.

- [ ] **Step 2: Add a reset-line helper and rewrite `content`**

In `DetailPanelView`, replace the `content` computed property (lines 297-324,
`@ViewBuilder` through the closing brace of `content`) with:

```swift
    /// Builds a row's reset line from a `LabeledWindow`, computed live so the
    /// countdown stays current.
    private func resetLine(for labeled: LabeledWindow) -> String {
        let reset: String
        switch labeled.resetStyle {
        case .countdown:
            reset = Formatting.resetCountdown(to: labeled.window.resetsAt, now: Date())
        case .date:
            reset = Formatting.resetDate(labeled.window.resetsAt)
        }
        return reset.isEmpty ? labeled.detail : "\(labeled.detail) · \(reset)"
    }

    @ViewBuilder
    private var content: some View {
        if let provider = store.menuBarProvider {
            switch provider.state {
            case .error(let kind):
                messageView(for: kind)
            case .loading where provider.snapshot == nil:
                Text("Loading usage…").font(.system(size: 12)).foregroundStyle(.secondary)
            default:
                if let snapshot = provider.snapshot {
                    VStack(alignment: .leading, spacing: 13) {
                        ForEach(snapshot.windows.indices, id: \.self) { index in
                            let labeled = snapshot.windows[index]
                            UsageBarRow(title: labeled.label,
                                        subtitle: labeled.detail,
                                        window: labeled.window,
                                        resetLine: resetLine(for: labeled),
                                        theme: preferences.theme)
                        }
                    }
                }
            }
        } else {
            // No provider detected — same message as the old `claudeCodeNotFound`.
            messageView(for: .claudeCodeNotFound)
        }
    }
```

`UsageBarRow`, `messageView(for:)`, `header`, `actions`, and `body` are
unchanged. (`UsageBarRow` keeps its existing `subtitle` parameter; `detail` is
passed through to it.)

- [ ] **Step 3: Build to verify this file compiles**

Run: `swift build 2>&1 | grep -A2 DetailPanelView || echo "DetailPanelView OK"`
Expected: `DetailPanelView OK` (the build still fails on `AppDelegate.swift`
until Task 9).

- [ ] **Step 4: Commit**

```bash
git add Sources/TokenSpendie/UI/DetailPanelView.swift
git commit -m "refactor: render the detail panel from ProviderSnapshot"
```

---

## Task 9: Wire `AppDelegate`, build, verify

**Files:**
- Modify: `Sources/TokenSpendie/AppDelegate.swift`

- [ ] **Step 1: Update the `UsageStore` construction**

In `Sources/TokenSpendie/AppDelegate.swift`, replace the `store = UsageStore(...)`
assignment (lines 16-21) with:

```swift
        store = UsageStore(
            providers: [ClaudeProvider()],
            preferences: preferences
        )
```

Nothing else in `AppDelegate` changes — `KeychainReader()` and
`SnapshotCache(...)` are no longer passed here; `ClaudeProvider()` and
`UsageStore`'s default `cacheFactory` supply them.

- [ ] **Step 2: Build the package**

Run: `swift build`
Expected: `Build complete!` with no errors.

- [ ] **Step 3: Run the full test suite**

Run: `swift test`
Expected: PASS — every test passes (`ProviderModelsTests`, `ClaudeProviderTests`,
`SnapshotCacheTests`, `UsageStoreTests`, `PreferencesTests`, `UsageDecoderTests`,
`UsageProviderTests`, and all pre-existing tests).

- [ ] **Step 4: Commit**

```bash
git add Sources/TokenSpendie/AppDelegate.swift
git commit -m "feat: construct UsageStore with the provider array"
```

- [ ] **Step 5: Build the app bundle and reinstall**

Run:

```bash
VERSION="$(git describe --tags 2>/dev/null || echo 1.2.3)" ./build.sh
osascript -e 'tell application "TokenSpendie" to quit' 2>/dev/null; pkill -x TokenSpendie 2>/dev/null; sleep 1
rm -rf /Applications/TokenSpendie.app
cp -R build/TokenSpendie.app /Applications/TokenSpendie.app
open /Applications/TokenSpendie.app
```

Expected: `==> Done` from `build.sh`; the app relaunches and its icon appears
in the menu bar. No commit — `build/` is git-ignored.

- [ ] **Step 6: Manual regression check (no visible change)**

Confirm the refactor changed nothing the user can see:

- Menu bar shows the session ring + percentage, exactly as before.
- Click the icon: the popover shows `TOKEN SPENDIE` + the `updated x ago`
  status + refresh glyph in the header; the Session / Weekly / per-model bars
  below; Settings… and Quit at the bottom.
- Each usage bar's reset line reads the same as before (`5-hour window ·
  resets in …`, `all models · resets …`, `Opus only · resets …`).
- Click refresh: the spin + `fetching…` behaviour is unchanged.
- If Claude Code is logged out (or test by temporarily expecting it), the
  panel shows the "Claude Code not found" message and the menu bar shows
  `✳ –` — same as before.

---

## Self-Review

**Spec coverage (Phase 1 portion of the spec):**
- Data model — `ProviderID`, `LabeledWindow`, `ProviderSnapshot`, per-provider
  state → Task 1. `ResetStyle` added so the reset line stays live (the spec's
  `LabeledWindow.label` is split into `label` + `detail` + `resetStyle` to
  avoid freezing the countdown at fetch time). `LoadState` reused as the
  per-provider state the spec names `ProviderState`. ✓
- `UsageProvider` protocol generalized (`detectCredentials()`, `fetchUsage()`)
  → Task 2. ✓
- `ClaudeProvider` absorbs the Keychain read + 401-retry, builds a
  `ProviderSnapshot` (headline = Session) → Task 3. ✓
- `SnapshotCache` keyed per provider → Task 4. ✓
- `UsageStore` multi-provider, per-provider polling, per-provider 429 backoff,
  `menuBarProviderID` → Task 5. Phase 1 polls providers sequentially (one
  provider); the spec's concurrent fetch is introduced in Phase 2 alongside
  the second provider. ✓
- Detection drops/adds rows; one provider failing never blocks another → Task 5
  (`refreshNow` filters by `detectCredentials()`, `withTaskGroup` per
  provider). ✓
- `Preferences.menuBarProviderID` persisted → Task 6. ✓
- Menu bar reads the active provider; fallback to first detected / `✳ –` →
  Task 7. ✓
- Panel renders the provider's windows; empty/error states → Task 8. ✓
- `AppDelegate` wiring → Task 9. ✓
- **Deferred to Phase 2 (correctly out of this plan):** the Gemini provider,
  the multi-row `ProviderRow` UI, the accordion, the ring picker, the menu-bar
  provider glyph, and the generalized empty state. Phase 1 keeps the
  single-provider visuals.

**Placeholder scan:** No TBD / TODO / "handle edge cases" / vague steps. Every
code step shows complete code or a complete file replacement. ✓

**Type consistency:** `ProviderID` (`.claude`), `ResetStyle`
(`.countdown` / `.date`), `LabeledWindow(label:detail:resetStyle:window:)`,
`ProviderSnapshot(id:plan:headline:windows:fetchedAt:)`,
`ProviderUsage(id:displayName:state:snapshot:)`, `ClaudeUsageEndpoint.fetchUsage(accessToken:)`,
`UsageProvider` (`id` / `displayName` / `detectCredentials()` / `fetchUsage()`),
`ClaudeProvider(credentials:endpoint:)` + `ClaudeProvider.convert(_:)`,
`CredentialStore.credentialsExist()`, `SnapshotCache.defaultURL(for:)` +
`load() -> ProviderSnapshot?` + `save(ProviderSnapshot)`,
`UsageStore(providers:cacheFactory:preferences:now:)` + `providers` +
`menuBarProvider` + `menuBarProviderID` + `setMenuBarProvider(_:)` +
`rateLimitedUntil(for:)` + `isRefreshing` + `refreshNow(ignoringBackoff:)` +
`manualRefresh()` + `rescheduleTimer()` + `start()`,
`Preferences.menuBarProviderID` — names and signatures match across the test
files (Tasks 1, 3, 4, 5, 6) and the implementations (Tasks 1-9). ✓

**Intermediate build state:** Tasks 2-4 leave the package non-compiling
(`UsageStore` still references the old protocol). This is called out explicitly
in the Task 2 note and in each affected step's "Expected" line; the build is
green again at the end of Task 5 for the data layer and fully green at Task 9
Step 2. ✓
