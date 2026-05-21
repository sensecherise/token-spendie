# Widget Theming & Manual Credentials Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add preset color themes and a manual credential-entry option to the existing Claude Usage Widget.

**Architecture:** A `Theme` enum maps usage tiers to colors; `Preferences` stores the chosen theme and credential mode; the menu bar ring and detail-panel bars read the theme. A new `ManualTokenStore` holds a user-pasted token in the app's own Keychain item, and a `CredentialRouter` picks between it and the existing `KeychainReader` by mode.

**Tech Stack:** Swift / Swift Package Manager, AppKit + SwiftUI, Security framework. Builds with Xcode-provided tooling; `swift test` for unit tests.

---

## Context for the engineer

Existing project on branch `main`, working app. Relevant files:

- `Sources/ClaudeUsageWidget/Model/UsageModels.swift` — `UsageWindow`, `ModelWeekly`, `UsageSnapshot`, `UsageError`, `ProviderError`, `LoadState`.
- `Sources/ClaudeUsageWidget/Data/OAuthCredentials.swift` — `OAuthCredentials`, `CredentialError` (`notFound`/`accessDenied`/`malformed`), `OAuthCredentialsParser`, `CredentialStore` protocol.
- `Sources/ClaudeUsageWidget/Data/KeychainReader.swift` — `KeychainReader: CredentialStore`.
- `Sources/ClaudeUsageWidget/Store/Preferences.swift` — `Preferences` (`@MainActor`, `ObservableObject`), `RefreshInterval`.
- `Sources/ClaudeUsageWidget/Store/UsageStore.swift` — `UsageStore`; `refreshNow()` maps errors to `LoadState`.
- `Sources/ClaudeUsageWidget/UI/Formatting.swift` — `UsageLevel` (`calm`/`warn`/`hot`, `forPercent`, `color`), `Formatting`.
- `Sources/ClaudeUsageWidget/UI/DetailPanelView.swift` — `UsageBarRow`, `DetailPanelView`.
- `Sources/ClaudeUsageWidget/UI/MenuBarController.swift` — status item, `ringImage`, dropdown panel.
- `Sources/ClaudeUsageWidget/UI/PreferencesView.swift` — `LoginItem`, `PreferencesView`.
- `Sources/ClaudeUsageWidget/AppDelegate.swift` — wires everything.

The full suite is **34 tests**; `swift test` must stay green after every task.

If `swift` is not found, prefix commands with `export PATH="$(dirname "$(xcrun --find swift)"):$PATH"`.

## File Structure

```
Sources/ClaudeUsageWidget/
├── UI/Theme.swift                  # NEW — Theme enum (presets → tier colors)
├── Data/ManualTokenStore.swift     # NEW — pasted token in the app's own Keychain item
├── Data/CredentialRouter.swift     # NEW — picks KeychainReader vs ManualTokenStore by mode
├── Model/UsageModels.swift         # MODIFIED — add UsageError.noManualToken
├── Store/Preferences.swift         # MODIFIED — add theme, credentialMode, CredentialMode
├── Store/UsageStore.swift          # MODIFIED — mode-aware notFound mapping
├── UI/Formatting.swift             # MODIFIED — remove UsageLevel.color; UsageLevel: Hashable
├── UI/DetailPanelView.swift        # MODIFIED — theme colors; noManualToken message
├── UI/MenuBarController.swift      # MODIFIED — theme color for the ring; take preferences
├── UI/PreferencesView.swift        # MODIFIED — APPEARANCE + CREDENTIAL sections
└── AppDelegate.swift               # MODIFIED — build router; pass preferences to MenuBarController
Tools/token-probe.swift             # NEW — one-off: verify a setup-token works
Tests/ClaudeUsageWidgetTests/
├── ThemeTests.swift                # NEW
├── ManualTokenStoreTests.swift     # NEW
├── CredentialRouterTests.swift     # NEW
├── PreferencesTests.swift          # MODIFIED — theme + credentialMode persistence
└── UsageStoreTests.swift           # MODIFIED — manual-mode notFound mapping
```

---

# Part 1 — Color Theming

## Task 1: Theme model

**Files:**
- Create: `Sources/ClaudeUsageWidget/UI/Theme.swift`
- Create: `Tests/ClaudeUsageWidgetTests/ThemeTests.swift`

- [ ] **Step 1: Write the failing test** — create `Tests/ClaudeUsageWidgetTests/ThemeTests.swift`:

```swift
import XCTest
@testable import ClaudeUsageWidget

final class ThemeTests: XCTestCase {
    func testFourThemes() {
        XCTAssertEqual(Theme.allCases.count, 4)
        XCTAssertTrue(Theme.allCases.contains(.default))
    }

    func testRawValueRoundTrip() {
        for theme in Theme.allCases {
            XCTAssertEqual(Theme(rawValue: theme.rawValue), theme)
        }
    }

    func testEachThemeHasDistinctTierColors() {
        for theme in Theme.allCases {
            XCTAssertNotEqual(theme.color(for: .calm), theme.color(for: .warn))
            XCTAssertNotEqual(theme.color(for: .warn), theme.color(for: .hot))
        }
    }

    func testThemesDifferFromEachOther() {
        XCTAssertNotEqual(Theme.default.color(for: .hot), Theme.ocean.color(for: .hot))
        XCTAssertNotEqual(Theme.ocean.color(for: .calm), Theme.sunset.color(for: .calm))
    }

    func testDisplayNames() {
        XCTAssertEqual(Theme.default.displayName, "Default")
        XCTAssertEqual(Theme.violet.displayName, "Violet")
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter ThemeTests`
Expected: FAIL — `cannot find 'Theme' in scope`.

- [ ] **Step 3: Write `Theme.swift`** — create `Sources/ClaudeUsageWidget/UI/Theme.swift`:

```swift
import SwiftUI

/// A preset color theme. Maps the three usage tiers to colors.
enum Theme: String, CaseIterable, Identifiable {
    case `default`
    case ocean
    case sunset
    case violet

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .default: return "Default"
        case .ocean:   return "Ocean"
        case .sunset:  return "Sunset"
        case .violet:  return "Violet"
        }
    }

    /// Tier colors: (calm `< 70%`, warn `70–90%`, hot `> 90%`).
    private var tierColors: (calm: Color, warn: Color, hot: Color) {
        switch self {
        case .default:
            return (Color(red: 0.373, green: 0.722, blue: 0.471),
                    Color(red: 0.878, green: 0.635, blue: 0.247),
                    Color(red: 0.851, green: 0.325, blue: 0.310))
        case .ocean:
            return (Color(red: 0.208, green: 0.753, blue: 0.651),
                    Color(red: 0.941, green: 0.741, blue: 0.353),
                    Color(red: 0.937, green: 0.435, blue: 0.424))
        case .sunset:
            return (Color(red: 0.941, green: 0.651, blue: 0.369),
                    Color(red: 0.925, green: 0.478, blue: 0.333),
                    Color(red: 0.851, green: 0.294, blue: 0.431))
        case .violet:
            return (Color(red: 0.435, green: 0.561, blue: 0.839),
                    Color(red: 0.663, green: 0.455, blue: 0.847),
                    Color(red: 0.851, green: 0.373, blue: 0.604))
        }
    }

    /// The color for a usage tier under this theme.
    func color(for level: UsageLevel) -> Color {
        switch level {
        case .calm: return tierColors.calm
        case .warn: return tierColors.warn
        case .hot:  return tierColors.hot
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter ThemeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidget/UI/Theme.swift Tests/ClaudeUsageWidgetTests/ThemeTests.swift
git commit -m "Add Theme model with four preset palettes"
```

---

## Task 2: Preferences — theme & credential mode

**Files:**
- Modify: `Sources/ClaudeUsageWidget/Store/Preferences.swift`
- Modify: `Tests/ClaudeUsageWidgetTests/PreferencesTests.swift`

- [ ] **Step 1: Write the failing test** — append these methods inside `final class PreferencesTests` in `Tests/ClaudeUsageWidgetTests/PreferencesTests.swift` (before the closing brace):

```swift
    @MainActor
    func testThemeDefaultsToDefault() {
        let prefs = Preferences(defaults: freshDefaults())
        XCTAssertEqual(prefs.theme, .default)
    }

    @MainActor
    func testThemePersists() {
        let defaults = freshDefaults()
        let first = Preferences(defaults: defaults)
        first.theme = .ocean
        let second = Preferences(defaults: defaults)
        XCTAssertEqual(second.theme, .ocean)
    }

    @MainActor
    func testCredentialModeDefaultsToAuto() {
        let prefs = Preferences(defaults: freshDefaults())
        XCTAssertEqual(prefs.credentialMode, .auto)
    }

    @MainActor
    func testCredentialModePersists() {
        let defaults = freshDefaults()
        let first = Preferences(defaults: defaults)
        first.credentialMode = .manual
        let second = Preferences(defaults: defaults)
        XCTAssertEqual(second.credentialMode, .manual)
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter PreferencesTests`
Expected: FAIL — `value of type 'Preferences' has no member 'theme'`.

- [ ] **Step 3: Add the `CredentialMode` enum and the two properties.**

In `Sources/ClaudeUsageWidget/Store/Preferences.swift`, add this enum at the top of the file, just after the existing `import` lines:

```swift
/// Which credential source the widget uses.
enum CredentialMode: String, CaseIterable, Identifiable {
    case auto
    case manual

    var id: String { rawValue }

    var label: String {
        switch self {
        case .auto:   return "Claude Code Keychain"
        case .manual: return "Manual token"
        }
    }
}
```

In the `Preferences` class, add two `@Published` properties next to the existing ones (after `launchAtLogin`):

```swift
    @Published var theme: Theme { didSet { defaults.set(theme.rawValue, forKey: Keys.theme) } }
    @Published var credentialMode: CredentialMode { didSet { defaults.set(credentialMode.rawValue, forKey: Keys.credentialMode) } }
```

In the `Keys` enum, add:

```swift
        static let theme = "theme"
        static let credentialMode = "credentialMode"
```

In `init(defaults:)`, add these lines at the end of the initializer body:

```swift
        let storedTheme = defaults.string(forKey: Keys.theme)
        self.theme = storedTheme.flatMap(Theme.init(rawValue:)) ?? .default
        let storedMode = defaults.string(forKey: Keys.credentialMode)
        self.credentialMode = storedMode.flatMap(CredentialMode.init(rawValue:)) ?? .auto
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter PreferencesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidget/Store/Preferences.swift Tests/ClaudeUsageWidgetTests/PreferencesTests.swift
git commit -m "Add theme and credentialMode to Preferences"
```

---

## Task 3: Wire the theme into the UI

Removes `UsageLevel.color` and updates every consumer to use `theme.color(for:)`. UI task — verified by `swift build` and the full test suite.

**Files:**
- Modify: `Sources/ClaudeUsageWidget/UI/Formatting.swift`
- Modify: `Sources/ClaudeUsageWidget/UI/DetailPanelView.swift`
- Modify: `Sources/ClaudeUsageWidget/UI/MenuBarController.swift`
- Modify: `Sources/ClaudeUsageWidget/AppDelegate.swift`

- [ ] **Step 1: `Formatting.swift` — make `UsageLevel` `Hashable`, remove `color`.**

Change the `UsageLevel` declaration line from `enum UsageLevel: Equatable {` to:

```swift
enum UsageLevel: Hashable {
```

Delete the entire `var color: Color { ... }` computed property from `UsageLevel` (the `switch self` block returning the three `Color(red:...)` values). Keep `forPercent(_:)`. The file still `import SwiftUI` (used elsewhere); leave the import.

- [ ] **Step 2: `DetailPanelView.swift` — `UsageBarRow` takes a theme.**

In `UsageBarRow`, add a stored property and change the `level`-derived color. Replace the property list + `level` computed var so the struct begins:

```swift
struct UsageBarRow: View {
    let title: String
    let subtitle: String
    let window: UsageWindow
    let resetLine: String
    var dimmed: Bool = false
    var theme: Theme

    private var level: UsageLevel { UsageLevel.forPercent(window.percent) }
    private var tierColor: Color { theme.color(for: level) }
    private var fraction: CGFloat { min(max(window.percent / 100, 0), 1) }
```

In `UsageBarRow.body`, replace both uses of `level.color` with `tierColor`:
- the percentage `Text(...)`: `.foregroundStyle(dimmed ? Color.secondary : tierColor)`
- the filled `Capsule().fill(dimmed ? Color.secondary : tierColor)`

In `DetailPanelView`, add an observed `preferences` property next to `store`:

```swift
    @ObservedObject var store: UsageStore
    @ObservedObject var preferences: Preferences
    var onRefresh: () -> Void
    var onOpenSettings: () -> Void
```

In `DetailPanelView`'s `content` view builder, every `UsageBarRow(...)` call must pass `theme: preferences.theme`. There are three call sites (Session, Weekly, and the `ForEach` model row) — add `theme: preferences.theme` as the final argument to each.

- [ ] **Step 3: `MenuBarController.swift` — take `preferences`, theme the ring.**

Add a stored property and update `init`:

```swift
    private let store: UsageStore
    private let preferences: Preferences
    private let onOpenSettings: () -> Void
```

```swift
    init(store: UsageStore, preferences: Preferences, onOpenSettings: @escaping () -> Void) {
        self.store = store
        self.preferences = preferences
        self.onOpenSettings = onOpenSettings
        super.init()
    }
```

In `refreshButton()`, replace the `default` case's color line:

```swift
        default:
            let percent = store.snapshot?.session.percent ?? 0
            let stale = store.state == .stale
            let color = stale
                ? NSColor.secondaryLabelColor
                : NSColor(preferences.theme.color(for: UsageLevel.forPercent(percent)))
            button.image = Self.ringImage(percent: percent, color: color)
            button.title = " \(Int(percent.rounded()))%"
```

In `install()`, after the existing `storeObserver = ...` block, also observe preferences so a theme change re-renders the ring:

```swift
        themeObserver = preferences.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.refreshButton() }
        }
```

Add the property near `storeObserver`:

```swift
    private var themeObserver: AnyCancellable?
```

In `remove()`, add `themeObserver?.cancel(); themeObserver = nil` next to the `storeObserver` cleanup.

In `openPanel()`, the `DetailPanelView(...)` initializer must now pass `preferences:` — update that call to include `preferences: preferences` after `store: store`.

- [ ] **Step 4: `AppDelegate.swift` — pass `preferences` to `MenuBarController`.**

Change the `menuBar = MenuBarController(...)` line to:

```swift
        menuBar = MenuBarController(store: store, preferences: preferences,
                                   onOpenSettings: { [weak self] in self?.showSettings() })
```

`FloatingPanelController` also builds a `DetailPanelView`. In `Sources/ClaudeUsageWidget/UI/FloatingPanelController.swift`, the `DetailPanelView(...)` call must pass `preferences:`. `FloatingPanelController` does not currently hold `preferences` — add it: a `private let preferences: Preferences` property, add `preferences` to its `init` signature, and in `AppDelegate` update the `floatingPanel = FloatingPanelController(...)` call to pass `preferences: preferences`. Then in `FloatingPanelController.show()`, the `DetailPanelView(...)` gets `preferences: preferences`.

- [ ] **Step 5: Build and test**

Run: `swift build`
Expected: `Build complete!`
Run: `swift test`
Expected: all tests pass (34).

- [ ] **Step 6: Commit**

```bash
git add Sources/ClaudeUsageWidget
git commit -m "Color the ring and bars from the selected theme"
```

---

## Task 4: PreferencesView — APPEARANCE section

UI task — verified by `swift build`.

**Files:**
- Modify: `Sources/ClaudeUsageWidget/UI/PreferencesView.swift`

- [ ] **Step 1: Add a theme-picker section.**

In `PreferencesView.body`, add this block immediately after the existing "REFRESH" `VStack` and before the `Toggle("Launch at login", ...)`:

```swift
            VStack(alignment: .leading, spacing: 8) {
                Text("APPEARANCE").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                HStack(spacing: 8) {
                    ForEach(Theme.allCases) { theme in
                        Button { preferences.theme = theme } label: {
                            VStack(spacing: 4) {
                                HStack(spacing: 2) {
                                    swatch(theme.color(for: .calm))
                                    swatch(theme.color(for: .warn))
                                    swatch(theme.color(for: .hot))
                                }
                                Text(theme.displayName).font(.system(size: 9))
                            }
                            .padding(6)
                            .background(
                                RoundedRectangle(cornerRadius: 6)
                                    .fill(preferences.theme == theme
                                          ? Color.accentColor.opacity(0.25)
                                          : Color.clear)
                            )
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
```

Add this helper method inside `PreferencesView`, after `body`:

```swift
    private func swatch(_ color: Color) -> some View {
        RoundedRectangle(cornerRadius: 3).fill(color).frame(width: 14, height: 14)
    }
```

The widget already has `frame(width: 300)` on the settings view; the four swatch-trios fit. If the build reports the view is too wide, raise the window/content width in `AppDelegate.showSettings()` and the `PreferencesView` `.frame` to `340` — change both.

- [ ] **Step 2: Build**

Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 3: Commit**

```bash
git add Sources/ClaudeUsageWidget/UI/PreferencesView.swift
git commit -m "Add theme picker to Preferences"
```

---

# Part 2 — Manual Credential Entry

## Task 5: Verify setup-token, then build `ManualTokenStore`

**Files:**
- Create: `Tools/token-probe.swift`
- Create: `Sources/ClaudeUsageWidget/Data/ManualTokenStore.swift`
- Create: `Tests/ClaudeUsageWidgetTests/ManualTokenStoreTests.swift`

- [ ] **Step 1: Write the verification probe** — create `Tools/token-probe.swift`:

```swift
// Tools/token-probe.swift — run with: swift Tools/token-probe.swift <token>
// Verifies a Claude OAuth token works against the usage endpoint.
import Foundation

guard CommandLine.arguments.count == 2 else {
    print("usage: swift Tools/token-probe.swift <token>")
    exit(2)
}
let token = CommandLine.arguments[1]
var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
request.httpMethod = "GET"
request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
request.setValue("application/json", forHTTPHeaderField: "Accept")
request.setValue("ClaudeUsageWidget/token-probe", forHTTPHeaderField: "User-Agent")

let semaphore = DispatchSemaphore(value: 0)
URLSession.shared.dataTask(with: request) { data, response, error in
    defer { semaphore.signal() }
    if let error = error { print("error: \(error)"); return }
    let http = response as! HTTPURLResponse
    print("HTTP \(http.statusCode)")
    if let data = data, let body = String(data: data, encoding: .utf8) { print(body) }
}.resume()
semaphore.wait()
```

- [ ] **Step 2: Verify a setup-token works**

Run, in order:
```bash
claude setup-token
```
Copy the printed token, then:
```bash
swift Tools/token-probe.swift '<paste-token>'
```
Expected: `HTTP 200` and a JSON body with `five_hour` / `seven_day`.

If the status is `200`, the manual-token path is sound — continue. If it is `401`/`403`, STOP and report NEEDS_CONTEXT: a `claude setup-token` token is not accepted by this endpoint and the manual-credentials feature needs rethinking before proceeding.

- [ ] **Step 3: Write the failing test** — create `Tests/ClaudeUsageWidgetTests/ManualTokenStoreTests.swift`:

```swift
import XCTest
@testable import ClaudeUsageWidget

final class ManualTokenStoreTests: XCTestCase {
    private func testStore() -> ManualTokenStore {
        ManualTokenStore(service: "ClaudeUsageWidget-Test-\(UUID().uuidString)")
    }

    func testSaveThenLoadRoundTrips() throws {
        let store = testStore()
        defer { store.clear() }
        try store.save(token: "tok-abc-123")
        XCTAssertEqual(try store.loadCredentials().accessToken, "tok-abc-123")
    }

    func testLoadWithNoTokenThrowsNotFound() {
        let store = testStore()
        XCTAssertThrowsError(try store.loadCredentials()) { error in
            XCTAssertEqual(error as? CredentialError, .notFound)
        }
    }

    func testSaveTrimsWhitespace() throws {
        let store = testStore()
        defer { store.clear() }
        try store.save(token: "  tok-xyz \n")
        XCTAssertEqual(try store.loadCredentials().accessToken, "tok-xyz")
    }

    func testSaveEmptyThrowsMalformed() {
        let store = testStore()
        XCTAssertThrowsError(try store.save(token: "   ")) { error in
            XCTAssertEqual(error as? CredentialError, .malformed)
        }
    }

    func testClearRemovesToken() throws {
        let store = testStore()
        try store.save(token: "tok")
        store.clear()
        XCTAssertThrowsError(try store.loadCredentials())
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `swift test --filter ManualTokenStoreTests`
Expected: FAIL — `cannot find 'ManualTokenStore' in scope`.

- [ ] **Step 5: Write `ManualTokenStore.swift`** — create `Sources/ClaudeUsageWidget/Data/ManualTokenStore.swift`:

```swift
import Foundation
import Security

/// Stores a user-pasted Claude OAuth token in the app's own Keychain item.
/// Because the app creates and owns this item, reading it never prompts.
final class ManualTokenStore: CredentialStore {
    let service: String

    init(service: String = "com.cherise.ClaudeUsage.token") {
        self.service = service
    }

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service]
    }

    func loadCredentials() throws -> OAuthCredentials {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        switch status {
        case errSecSuccess:
            guard let data = result as? Data,
                  let token = String(data: data, encoding: .utf8),
                  !token.isEmpty else {
                throw CredentialError.malformed
            }
            return OAuthCredentials(accessToken: token, refreshToken: nil, expiresAt: nil)
        case errSecItemNotFound:
            throw CredentialError.notFound
        default:
            throw CredentialError.accessDenied
        }
    }

    /// Saves (replacing any existing) token. Throws `.malformed` if blank.
    func save(token: String) throws {
        let trimmed = token.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { throw CredentialError.malformed }
        SecItemDelete(baseQuery as CFDictionary)
        var add = baseQuery
        add[kSecValueData as String] = Data(trimmed.utf8)
        let status = SecItemAdd(add as CFDictionary, nil)
        guard status == errSecSuccess else { throw CredentialError.accessDenied }
    }

    /// Removes the stored token, if any.
    func clear() {
        SecItemDelete(baseQuery as CFDictionary)
    }

    /// True if a non-empty token is currently stored.
    var hasToken: Bool {
        (try? loadCredentials()) != nil
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `swift test --filter ManualTokenStoreTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Tools/token-probe.swift Sources/ClaudeUsageWidget/Data/ManualTokenStore.swift Tests/ClaudeUsageWidgetTests/ManualTokenStoreTests.swift
git commit -m "Add ManualTokenStore for pasted credentials"
```

---

## Task 6: `noManualToken` error state

**Files:**
- Modify: `Sources/ClaudeUsageWidget/Model/UsageModels.swift`
- Modify: `Sources/ClaudeUsageWidget/Store/UsageStore.swift`
- Modify: `Sources/ClaudeUsageWidget/UI/DetailPanelView.swift`
- Modify: `Tests/ClaudeUsageWidgetTests/UsageStoreTests.swift`

- [ ] **Step 1: Write the failing test** — append inside `final class UsageStoreTests` in `Tests/ClaudeUsageWidgetTests/UsageStoreTests.swift` (before the closing brace):

```swift
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter UsageStoreTests`
Expected: FAIL — `type 'UsageError' has no member 'noManualToken'`.

- [ ] **Step 3: Add the error case.** In `Sources/ClaudeUsageWidget/Model/UsageModels.swift`, add a case to `enum UsageError`:

```swift
    case noManualToken          // manual mode selected but no token saved
```

- [ ] **Step 4: Make the `notFound` mapping mode-aware.** In `Sources/ClaudeUsageWidget/Store/UsageStore.swift`, in `refreshNow()`, replace the existing `catch CredentialError.notFound, CredentialError.malformed { ... }` block with:

```swift
        } catch CredentialError.notFound, CredentialError.malformed {
            state = .error(preferences.credentialMode == .manual ? .noManualToken : .claudeCodeNotFound)
        } catch CredentialError.accessDenied {
            state = .error(.keychainAccessDenied)
        }
```

(The `accessDenied` line is unchanged — it already follows; keep it exactly once.)

- [ ] **Step 5: Handle the new case in `DetailPanelView`.** In `Sources/ClaudeUsageWidget/UI/DetailPanelView.swift`, in `messageView(for:)`, add a case to the `switch kind`:

```swift
            case .noManualToken:
                return ("🔑", "No token saved. Open Settings, choose Manual, and paste a token from `claude setup-token`.")
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `swift test --filter UsageStoreTests`
Expected: PASS.
Run: `swift build`
Expected: `Build complete!` (confirms the `DetailPanelView` switch is exhaustive).

- [ ] **Step 7: Commit**

```bash
git add Sources/ClaudeUsageWidget Tests/ClaudeUsageWidgetTests/UsageStoreTests.swift
git commit -m "Add noManualToken error state"
```

---

## Task 7: CredentialRouter

**Files:**
- Create: `Sources/ClaudeUsageWidget/Data/CredentialRouter.swift`
- Create: `Tests/ClaudeUsageWidgetTests/CredentialRouterTests.swift`

- [ ] **Step 1: Write the failing test** — create `Tests/ClaudeUsageWidgetTests/CredentialRouterTests.swift`:

```swift
import XCTest
@testable import ClaudeUsageWidget

final class CredentialRouterTests: XCTestCase {
    private final class StubStore: CredentialStore {
        let result: Result<OAuthCredentials, Error>
        init(_ result: Result<OAuthCredentials, Error>) { self.result = result }
        func loadCredentials() throws -> OAuthCredentials { try result.get() }
    }

    private func creds(_ token: String) -> OAuthCredentials {
        OAuthCredentials(accessToken: token, refreshToken: nil, expiresAt: nil)
    }

    func testAutoModeUsesKeychain() throws {
        let router = CredentialRouter(
            mode: .auto,
            keychain: StubStore(.success(creds("from-keychain"))),
            manual: StubStore(.failure(CredentialError.notFound))
        )
        XCTAssertEqual(try router.loadCredentials().accessToken, "from-keychain")
    }

    func testManualModeUsesManualStore() throws {
        let router = CredentialRouter(
            mode: .manual,
            keychain: StubStore(.failure(CredentialError.notFound)),
            manual: StubStore(.success(creds("from-manual")))
        )
        XCTAssertEqual(try router.loadCredentials().accessToken, "from-manual")
    }

    func testModeIsMutable() throws {
        let router = CredentialRouter(
            mode: .auto,
            keychain: StubStore(.success(creds("k"))),
            manual: StubStore(.success(creds("m")))
        )
        router.mode = .manual
        XCTAssertEqual(try router.loadCredentials().accessToken, "m")
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `swift test --filter CredentialRouterTests`
Expected: FAIL — `cannot find 'CredentialRouter' in scope`.

- [ ] **Step 3: Write `CredentialRouter.swift`** — create `Sources/ClaudeUsageWidget/Data/CredentialRouter.swift`:

```swift
import Foundation

/// Routes credential loading to the Keychain reader (auto mode) or the
/// manual-token store (manual mode). `mode` is updated by `AppDelegate` when
/// the preference changes.
final class CredentialRouter: CredentialStore {
    var mode: CredentialMode
    private let keychain: CredentialStore
    private let manual: CredentialStore

    init(mode: CredentialMode, keychain: CredentialStore, manual: CredentialStore) {
        self.mode = mode
        self.keychain = keychain
        self.manual = manual
    }

    func loadCredentials() throws -> OAuthCredentials {
        switch mode {
        case .auto:   return try keychain.loadCredentials()
        case .manual: return try manual.loadCredentials()
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `swift test --filter CredentialRouterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidget/Data/CredentialRouter.swift Tests/ClaudeUsageWidgetTests/CredentialRouterTests.swift
git commit -m "Add CredentialRouter to switch credential sources"
```

---

## Task 8: Wire credentials into the app

UI/integration task — verified by `swift build`, `swift test`, and a launch smoke test.

**Files:**
- Modify: `Sources/ClaudeUsageWidget/AppDelegate.swift`

- [ ] **Step 1: Build and hold the router and manual store.**

In `AppDelegate`, add stored properties next to the existing ones:

```swift
    private var manualTokenStore: ManualTokenStore!
    private var credentialRouter: CredentialRouter!
```

In `applicationDidFinishLaunching`, replace the `store = UsageStore(...)` construction so the router is built first:

```swift
        manualTokenStore = ManualTokenStore()
        credentialRouter = CredentialRouter(
            mode: preferences.credentialMode,
            keychain: KeychainReader(),
            manual: manualTokenStore
        )
        store = UsageStore(
            provider: EndpointUsageProvider(),
            credentials: credentialRouter,
            cache: SnapshotCache(fileURL: SnapshotCache.defaultURL()),
            preferences: preferences
        )
```

- [ ] **Step 2: Keep the router's mode in sync with preferences.**

In the existing `preferences.objectWillChange` sink in `applicationDidFinishLaunching`, the closure currently calls `applyDisplayPreferences()`. Replace that closure body so it also syncs the router and refreshes when the mode changes:

```swift
        preferences.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] in
                guard let self else { return }
                self.applyDisplayPreferences()
                let newMode = self.preferences.credentialMode
                if self.credentialRouter.mode != newMode {
                    self.credentialRouter.mode = newMode
                    Task { await self.store.refreshNow() }
                }
            }
            .store(in: &cancellables)
```

- [ ] **Step 3: Expose the manual store to `PreferencesView`.**

`PreferencesView` (Task 9) needs to save the pasted token. In `showSettings()`, the `PreferencesView(...)` initializer will gain a `manualTokenStore:` argument (added in Task 9). For now, no change — Task 9 updates this call.

- [ ] **Step 4: Build and test**

Run: `swift build`
Expected: `Build complete!`
Run: `swift test`
Expected: all tests pass.

- [ ] **Step 5: Smoke test**

```bash
swift run ClaudeUsageWidget > /tmp/cuw-run.log 2>&1 &
RUNPID=$!
sleep 8
kill -0 $RUNPID 2>/dev/null && echo "ALIVE" || { echo "DEAD"; cat /tmp/cuw-run.log; }
kill $RUNPID 2>/dev/null; pkill -f ClaudeUsageWidget 2>/dev/null
```
Expected: `ALIVE`.

- [ ] **Step 6: Commit**

```bash
git add Sources/ClaudeUsageWidget/AppDelegate.swift
git commit -m "Wire CredentialRouter and manual token store into the app"
```

---

## Task 9: PreferencesView — CREDENTIAL section

UI task — verified by `swift build`.

**Files:**
- Modify: `Sources/ClaudeUsageWidget/UI/PreferencesView.swift`
- Modify: `Sources/ClaudeUsageWidget/AppDelegate.swift`

- [ ] **Step 1: Give `PreferencesView` the manual store and a draft field.**

In `PreferencesView`, add a stored property and a state field, next to the existing `@ObservedObject var preferences`:

```swift
    let manualTokenStore: ManualTokenStore
    @State private var draftToken: String = ""
    @State private var saveConfirmation: String = ""
```

- [ ] **Step 2: Add the CREDENTIAL section to `body`.**

In `PreferencesView.body`, add this block after the "APPEARANCE" `VStack` and before the `Toggle("Launch at login", ...)`:

```swift
            VStack(alignment: .leading, spacing: 8) {
                Text("CREDENTIAL").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                Picker("Source", selection: $preferences.credentialMode) {
                    ForEach(CredentialMode.allCases) { Text($0.label).tag($0) }
                }
                if preferences.credentialMode == .manual {
                    Text("Run `claude setup-token` in Terminal, then paste the token below.")
                        .font(.system(size: 10)).foregroundStyle(.secondary)
                    SecureField("Paste token", text: $draftToken)
                    HStack {
                        Button("Save token") {
                            do {
                                try manualTokenStore.save(token: draftToken)
                                draftToken = ""
                                saveConfirmation = "Saved."
                            } catch {
                                saveConfirmation = "Token cannot be empty."
                            }
                        }
                        Text(saveConfirmation)
                            .font(.system(size: 10)).foregroundStyle(.secondary)
                    }
                }
            }
```

- [ ] **Step 3: Pass the manual store from `AppDelegate`.**

In `AppDelegate.showSettings()`, update the `PreferencesView(...)` initializer to include `manualTokenStore: manualTokenStore`:

```swift
        let view = PreferencesView(
            preferences: preferences,
            manualTokenStore: manualTokenStore,
            onDisplayChanged: { [weak self] in self?.applyDisplayPreferences() },
            onIntervalChanged: { [weak self] in self?.store.rescheduleTimer() }
        )
```

- [ ] **Step 4: Build**

Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 5: Commit**

```bash
git add Sources/ClaudeUsageWidget/UI/PreferencesView.swift Sources/ClaudeUsageWidget/AppDelegate.swift
git commit -m "Add credential source section to Preferences"
```

---

## Task 10: Final build, packaging & manual QA

**Files:** none created.

- [ ] **Step 1: Full test + build**

Run: `swift test`
Expected: all tests pass.
Run: `swift build`
Expected: `Build complete!`

- [ ] **Step 2: Package**

Run: `./build.sh`
Expected: ends with `Done: build/ClaudeUsage.app  and  build/ClaudeUsage.zip`.

- [ ] **Step 3: Manual QA**

```bash
open build/ClaudeUsage.app
```
Confirm:
1. Menu bar ring shows in the Default theme.
2. Settings → APPEARANCE: pick each of Ocean / Sunset / Violet — the ring and the detail-panel bars recolor immediately.
3. Settings → CREDENTIAL: switch to Manual with no token saved — the panel shows the "No token saved" message.
4. Run `claude setup-token`, paste the token, Save — the widget recovers and shows usage.
5. Switch back to Auto — usage still shows.

- [ ] **Step 4: Commit any packaging artifacts** (none expected; `build/` is git-ignored). If `git status` is clean, nothing to commit.

---

## Self-Review

**Spec coverage:**
- Theme model (4 presets, color table) → Task 1.
- `Preferences.theme` → Task 2; wiring into ring/bars/text → Task 3; theme picker UI → Task 4.
- Theme persistence, `.default` fallback → Task 2.
- `ManualTokenStore` in the app's own Keychain item → Task 5.
- `claude setup-token` as the pasted credential; risk verified → Task 5 Step 2.
- `CredentialMode` + `Preferences.credentialMode` → Task 2; `CredentialRouter` → Task 7; wiring → Task 8.
- "No token saved" error state → Task 6.
- CREDENTIAL Preferences section (mode picker, secure field, help text) → Task 9.
- Mode switch refreshes `UsageStore` → Task 8 Step 2.
- Testing (Theme, Preferences, ManualTokenStore, CredentialRouter, UsageStore mapping) → Tasks 1, 2, 5, 6, 7.

**Placeholder scan:** none — every step has complete code or exact commands.

**Type consistency:** `Theme` (Task 1) used in `Preferences` (Task 2), `UsageBarRow`/`MenuBarController` (Task 3), `PreferencesView` (Task 4). `CredentialMode` (Task 2) used in `CredentialRouter` (Task 7), `UsageStore` (Task 6), `AppDelegate` (Task 8). `ManualTokenStore` (Task 5) used in `CredentialRouter` (Task 7), `AppDelegate` (Task 8), `PreferencesView` (Task 9). `UsageError.noManualToken` (Task 6) handled in `DetailPanelView` (Task 6) and reached via `UsageStore` (Task 6). `UsageLevel` made `Hashable` (Task 3) for the `ForEach` in the theme picker (Task 4). Signatures consistent.
