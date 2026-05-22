# Dropdown UI Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the menu bar dropdown behave like a native macOS menu-bar app — highlight the icon while open, give actions their own section with a Quit button, add hover states, and animate + throttle the refresh button.

**Architecture:** Three changes. (1) `UsageStore` gains an `isRefreshing` published flag and a throttled `manualRefresh()`. (2) `MenuBarController` toggles the status item's native highlight around the panel. (3) `DetailPanelView` is restructured into header / content / actions section / status strip, with reusable hover-aware `RefreshButton` and `MenuActionRow` subviews; its hosts pass a new `onQuit` closure and route the refresh button through `manualRefresh()`.

**Tech Stack:** Swift 5.9, SwiftUI + AppKit, Swift Package Manager, XCTest. macOS 13+ deployment target.

**Branch:** `dropdown-ui-polish` (already created and checked out).

---

## File Structure

| File | Responsibility | Change |
|------|----------------|--------|
| `Sources/TokenSpendie/Store/UsageStore.swift` | Polling + published state | Add `isRefreshing` flag, `manualRefresh()` with 2 s throttle |
| `Tests/TokenSpendieTests/UsageStoreTests.swift` | Store unit tests | Add `ProbeProvider` double + 4 tests |
| `Sources/TokenSpendie/UI/MenuBarController.swift` | Status item + dropdown panel | Highlight status item on open/close; new `onQuit`; route refresh to `manualRefresh()` |
| `Sources/TokenSpendie/UI/DetailPanelView.swift` | The dropdown's SwiftUI content | Actions section + status strip; `RefreshButton` + `MenuActionRow`; `onQuit` property |
| `Sources/TokenSpendie/UI/FloatingPanelController.swift` | Optional floating panel | New `onQuit`; route refresh to `manualRefresh()` |
| `Sources/TokenSpendie/AppDelegate.swift` | App wiring | Provide `onQuit` (`NSApp.terminate`) to both controllers |

---

## Task 1: `UsageStore` — `isRefreshing` flag + throttled `manualRefresh()`

**Files:**
- Modify: `Sources/TokenSpendie/Store/UsageStore.swift`
- Test: `Tests/TokenSpendieTests/UsageStoreTests.swift`

- [ ] **Step 1: Add the `ProbeProvider` test double**

In `Tests/TokenSpendieTests/UsageStoreTests.swift`, immediately after the
`StubProvider` class closing brace (currently line 24), add:

```swift
    /// A provider that runs a probe closure at the moment `fetchUsage` is called,
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
```

- [ ] **Step 2: Write the failing tests**

In the same file, insert these four tests just before the final closing `}` of
the `UsageStoreTests` class:

```swift
    func testIsRefreshingTrueDuringFetchAndFalseAfter() async {
        let provider = ProbeProvider(.success(snapshot(20)))
        let store = makeStore(credentials: StubCredentials(.success(creds())),
                              provider: provider)
        var observedDuringFetch = false
        provider.onFetch = { observedDuringFetch = store.isRefreshing }
        XCTAssertFalse(store.isRefreshing, "idle before any refresh")
        await store.refreshNow()
        XCTAssertTrue(observedDuringFetch, "isRefreshing is true while the fetch runs")
        XCTAssertFalse(store.isRefreshing, "isRefreshing clears after the refresh")
    }

    func testIsRefreshingClearsWhenFetchThrows() async {
        let store = makeStore(credentials: StubCredentials(.success(creds())),
                              provider: StubProvider([.failure(ProviderError.network)]))
        await store.refreshNow()
        XCTAssertFalse(store.isRefreshing, "isRefreshing clears even when the fetch fails")
    }

    func testManualRefreshIgnoresRapidRepeatCalls() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider([.success(snapshot(10))])
        let store = makeStore(credentials: StubCredentials(.success(creds())),
                              provider: provider, now: { clock })
        await store.manualRefresh()                       // fires
        await store.manualRefresh()                       // within 2s gap — skipped
        XCTAssertEqual(provider.callCount, 1, "a second manual refresh within 2s is ignored")
        clock = Date(timeIntervalSince1970: 3)            // past the gap
        await store.manualRefresh()                       // fires again
        XCTAssertEqual(provider.callCount, 2, "a manual refresh after the gap runs")
    }

    func testManualRefreshSkippedDuringRateLimitBackoff() async {
        var clock = Date(timeIntervalSince1970: 0)
        let provider = StubProvider([.success(snapshot(30)),
                                     .failure(ProviderError.rateLimited(retryAfter: 600))])
        let store = makeStore(credentials: StubCredentials(.success(creds())),
                              provider: provider, now: { clock })
        await store.manualRefresh()                       // success
        clock = Date(timeIntervalSince1970: 3)
        await store.manualRefresh()                       // 429 — backoff begins
        let callsAfter429 = provider.callCount
        clock = Date(timeIntervalSince1970: 6)
        await store.manualRefresh()                       // backoff active — must skip
        XCTAssertEqual(provider.callCount, callsAfter429,
                       "a manual refresh during backoff makes no request")
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `swift test --filter UsageStoreTests`
Expected: FAIL — compile error, `value of type 'UsageStore' has no member 'isRefreshing'` and `no member 'manualRefresh'`.

- [ ] **Step 4: Add the `isRefreshing` published property**

In `Sources/TokenSpendie/Store/UsageStore.swift`, replace:

```swift
    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var state: LoadState = .loading
```

with:

```swift
    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var state: LoadState = .loading
    /// True while a refresh cycle is running. Drives the refresh icon's spin
    /// animation and disables the button so it cannot be spammed.
    @Published private(set) var isRefreshing = false
```

- [ ] **Step 5: Add the throttle state**

In the same file, replace:

```swift
    private var backoffUntil: Date?
    private var consecutiveRateLimits = 0
```

with:

```swift
    private var backoffUntil: Date?
    private var consecutiveRateLimits = 0
    /// Timestamp of the last user-initiated refresh. Manual refreshes within
    /// `manualRefreshMinGap` of this are ignored, keeping the button un-spammable.
    private var lastManualRefresh: Date?
    private static let manualRefreshMinGap: TimeInterval = 2
```

- [ ] **Step 6: Set `isRefreshing` inside `refreshNow()`**

In the same file, replace:

```swift
    func refreshNow() async {
        if let backoffUntil, now() < backoffUntil { return }
        if snapshot == nil { state = .loading }
        do {
```

with:

```swift
    func refreshNow() async {
        if let backoffUntil, now() < backoffUntil { return }
        isRefreshing = true
        defer { isRefreshing = false }
        if snapshot == nil { state = .loading }
        do {
```

The `defer` clears the flag on every exit path — success, error, and thrown
errors. The early `backoffUntil` return runs before the flag is set, so it stays
`false` when no work happens.

- [ ] **Step 7: Add `manualRefresh()`**

In the same file, immediately after the closing `}` of `refreshNow()` (the line
before the `setPanelVisible` doc comment), add:

```swift
    /// A user-initiated refresh from the refresh button. Ignored while a refresh
    /// is already running, or if the previous manual refresh was under
    /// `manualRefreshMinGap` seconds ago — together these keep the button
    /// un-spammable. Other refresh triggers (timer, wake, reconnect) bypass this
    /// and call `refreshNow()` directly.
    func manualRefresh() async {
        if isRefreshing { return }
        if let lastManualRefresh,
           now().timeIntervalSince(lastManualRefresh) < Self.manualRefreshMinGap {
            return
        }
        lastManualRefresh = now()
        await refreshNow()
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `swift test --filter UsageStoreTests`
Expected: PASS — all `UsageStoreTests` tests green, including the four new ones.

- [ ] **Step 9: Commit**

```bash
git add Sources/TokenSpendie/Store/UsageStore.swift Tests/TokenSpendieTests/UsageStoreTests.swift
git commit -m "$(cat <<'EOF'
Add isRefreshing flag and un-spammable manual refresh

isRefreshing tracks an in-flight refresh cycle. manualRefresh() ignores
calls while one is running or within 2s of the last manual refresh, so
the refresh button cannot be spammed.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Status item backdrop — highlight the icon while the dropdown is open

**Files:**
- Modify: `Sources/TokenSpendie/UI/MenuBarController.swift`

`NSStatusBarButton.highlight(_:)` draws the standard rounded macOS selection
backdrop behind the icon. This task is visual — verified by build + manual QA, no
unit test.

- [ ] **Step 1: Highlight the status item when the panel opens**

In `Sources/TokenSpendie/UI/MenuBarController.swift`, inside `openPanel()`,
replace:

```swift
        panel.makeKeyAndOrderFront(nil)
        self.panel = panel
        store.setPanelVisible(true, source: .menuBar)
```

with:

```swift
        panel.makeKeyAndOrderFront(nil)
        self.panel = panel
        button.highlight(true)
        store.setPanelVisible(true, source: .menuBar)
```

(`button` is already in scope from the `guard let button = statusItem?.button`
at the top of `openPanel()`.)

- [ ] **Step 2: Clear the highlight when the panel closes**

In the same file, inside `closePanel()`, replace:

```swift
    private func closePanel() {
        if let clickMonitor {
            NSEvent.removeMonitor(clickMonitor)
            self.clickMonitor = nil
        }
        panel?.orderOut(nil)
```

with:

```swift
    private func closePanel() {
        if let clickMonitor {
            NSEvent.removeMonitor(clickMonitor)
            self.clickMonitor = nil
        }
        statusItem?.button?.highlight(false)
        panel?.orderOut(nil)
```

`closePanel()` is the single funnel for every dismiss path (toggle, outside
click, opening Settings), so clearing the highlight here covers all of them.

- [ ] **Step 3: Build to verify it compiles**

Run: `swift build`
Expected: `Build complete!` with no errors.

- [ ] **Step 4: Commit**

```bash
git add Sources/TokenSpendie/UI/MenuBarController.swift
git commit -m "$(cat <<'EOF'
Highlight the status item while the dropdown is open

Toggle the status button's native selection backdrop on panel open/close,
so the menu bar icon reads as active like a real menu-bar app.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Dropdown layout — actions section, Quit, hover states, refresh animation

**Files:**
- Modify (full rewrite): `Sources/TokenSpendie/UI/DetailPanelView.swift`
- Modify: `Sources/TokenSpendie/UI/FloatingPanelController.swift`
- Modify: `Sources/TokenSpendie/UI/MenuBarController.swift`
- Modify: `Sources/TokenSpendie/AppDelegate.swift`

This is one atomic change: `DetailPanelView` gains an `onQuit` parameter, so the
project does not compile until every caller is updated. All edits land in one
commit.

- [ ] **Step 1: Rewrite `DetailPanelView.swift`**

Replace the entire contents of `Sources/TokenSpendie/UI/DetailPanelView.swift`
with:

```swift
import SwiftUI

/// One labelled progress bar with its reset line.
struct UsageBarRow: View {
    let title: String
    let subtitle: String
    let window: UsageWindow
    let resetLine: String
    var theme: Theme

    private var level: UsageLevel { UsageLevel.forPercent(window.percent) }
    private var tierColor: Color { theme.color(for: level) }
    private var fraction: CGFloat { min(max(window.percent / 100, 0), 1) }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Text(title).font(.system(size: 12, weight: .semibold))
                Spacer()
                Text("\(Int(window.percent.rounded()))%")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(tierColor)
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.primary.opacity(0.12))
                    Capsule()
                        .fill(tierColor)
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

/// The header refresh control: spins while a refresh runs, is disabled then, and
/// shows a subtle rounded background on hover.
private struct RefreshButton: View {
    @ObservedObject var store: UsageStore
    var onRefresh: () -> Void
    @State private var hovering = false
    @State private var angle = 0.0

    var body: some View {
        Button(action: onRefresh) {
            Image(systemName: "arrow.clockwise")
                .font(.system(size: 11, weight: .semibold))
                .rotationEffect(.degrees(angle))
                .frame(width: 20, height: 20)
                .background(
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(Color.primary.opacity(hovering ? 0.12 : 0))
                )
        }
        .buttonStyle(.plain)
        .disabled(store.isRefreshing)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.12), value: hovering)
        .onAppear { if store.isRefreshing { startSpin() } }
        .onChange(of: store.isRefreshing) { refreshing in
            if refreshing { startSpin() } else { stopSpin() }
        }
    }

    /// Begins a continuous rotation that repeats until `stopSpin()` is called.
    private func startSpin() {
        angle = 0
        withAnimation(.linear(duration: 1).repeatForever(autoreverses: false)) {
            angle = 360
        }
    }

    /// Eases the icon back to its rest position.
    private func stopSpin() {
        withAnimation(.linear(duration: 0.2)) { angle = 0 }
    }
}

/// A full-width action row in the dropdown's actions section: leading icon,
/// label, and a subtle rounded highlight on hover.
private struct MenuActionRow: View {
    let systemImage: String
    let title: String
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                Image(systemName: systemImage)
                    .font(.system(size: 11))
                    .frame(width: 14)
                Text(title).font(.system(size: 12))
                Spacer()
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .background(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(Color.primary.opacity(hovering ? 0.09 : 0))
            )
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.12), value: hovering)
    }
}

/// The full detail panel: header, usage rows, actions section, status strip.
struct DetailPanelView: View {
    @ObservedObject var store: UsageStore
    @ObservedObject var preferences: Preferences
    var onRefresh: () -> Void
    var onOpenSettings: () -> Void
    var onQuit: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Divider()
            content.padding(13)
            Divider()
            actions
            Divider()
            statusStrip
        }
        .frame(width: 260)
    }

    private var header: some View {
        HStack {
            Text("TOKEN SPENDIE")
                .font(.system(size: 10, weight: .heavy)).kerning(0.5)
            Spacer()
            RefreshButton(store: store, onRefresh: onRefresh)
        }
        .padding(.horizontal, 10).padding(.vertical, 6)
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
                VStack(alignment: .leading, spacing: 13) {
                    UsageBarRow(title: "Session", subtitle: "5-hour window",
                                window: snapshot.session,
                                resetLine: "5-hour window · " + Formatting.resetCountdown(to: snapshot.session.resetsAt, now: Date()),
                                theme: preferences.theme)
                    UsageBarRow(title: "Weekly", subtitle: "all models",
                                window: snapshot.weekly,
                                resetLine: "all models · " + Formatting.resetDate(snapshot.weekly.resetsAt),
                                theme: preferences.theme)
                    ForEach(snapshot.modelWeeklies, id: \.model) { item in
                        UsageBarRow(title: "Weekly · \(item.model)", subtitle: item.model,
                                    window: item.window,
                                    resetLine: "\(item.model) only · " + Formatting.resetDate(item.window.resetsAt),
                                    theme: preferences.theme)
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
            case .noManualToken:
                return ("🔑", "No token saved. Open Settings, choose Manual, and paste a token from `claude setup-token`.")
            }
        }()
        return HStack(alignment: .top, spacing: 8) {
            Text(icon).font(.system(size: 15))
            Text(text).font(.system(size: 11)).foregroundStyle(.primary.opacity(0.85))
        }
    }

    private var actions: some View {
        VStack(spacing: 0) {
            MenuActionRow(systemImage: "gearshape", title: "Settings…", action: onOpenSettings)
            MenuActionRow(systemImage: "power", title: "Quit", action: onQuit)
        }
        .padding(.horizontal, 5)
        .padding(.vertical, 4)
    }

    private var statusStrip: some View {
        Text(statusText)
            .font(.system(size: 9))
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 13)
            .padding(.vertical, 7)
    }

    private var statusText: String {
        guard let snapshot = store.snapshot else { return " " }
        let ago = Formatting.updatedAgo(snapshot.fetchedAt, now: Date())
        return store.state == .stale ? "offline — \(ago)" : ago
    }
}
```

- [ ] **Step 2: Update `FloatingPanelController.swift`**

In `Sources/TokenSpendie/UI/FloatingPanelController.swift`, replace:

```swift
    private let store: UsageStore
    private let preferences: Preferences
    private let onOpenSettings: () -> Void
    private var panel: NSPanel?

    init(store: UsageStore, preferences: Preferences, onOpenSettings: @escaping () -> Void) {
        self.store = store
        self.preferences = preferences
        self.onOpenSettings = onOpenSettings
    }
```

with:

```swift
    private let store: UsageStore
    private let preferences: Preferences
    private let onOpenSettings: () -> Void
    private let onQuit: () -> Void
    private var panel: NSPanel?

    init(store: UsageStore, preferences: Preferences,
         onOpenSettings: @escaping () -> Void, onQuit: @escaping () -> Void) {
        self.store = store
        self.preferences = preferences
        self.onOpenSettings = onOpenSettings
        self.onQuit = onQuit
    }
```

Then, in the same file, replace:

```swift
            rootView: DetailPanelView(
                store: store,
                preferences: preferences,
                onRefresh: { [weak self] in Task { await self?.store.refreshNow() } },
                onOpenSettings: { [weak self] in self?.onOpenSettings() }
            )
```

with:

```swift
            rootView: DetailPanelView(
                store: store,
                preferences: preferences,
                onRefresh: { [weak self] in Task { await self?.store.manualRefresh() } },
                onOpenSettings: { [weak self] in self?.onOpenSettings() },
                onQuit: { [weak self] in self?.onQuit() }
            )
```

- [ ] **Step 3: Update `MenuBarController.swift`**

In `Sources/TokenSpendie/UI/MenuBarController.swift`, replace:

```swift
    private let store: UsageStore
    private let preferences: Preferences
    private let onOpenSettings: () -> Void
    private var statusItem: NSStatusItem?
```

with:

```swift
    private let store: UsageStore
    private let preferences: Preferences
    private let onOpenSettings: () -> Void
    private let onQuit: () -> Void
    private var statusItem: NSStatusItem?
```

Then replace:

```swift
    init(store: UsageStore, preferences: Preferences, onOpenSettings: @escaping () -> Void) {
        self.store = store
        self.preferences = preferences
        self.onOpenSettings = onOpenSettings
        super.init()
    }
```

with:

```swift
    init(store: UsageStore, preferences: Preferences,
         onOpenSettings: @escaping () -> Void, onQuit: @escaping () -> Void) {
        self.store = store
        self.preferences = preferences
        self.onOpenSettings = onOpenSettings
        self.onQuit = onQuit
        super.init()
    }
```

Then, inside `openPanel()`, replace:

```swift
        let content = DetailPanelView(
            store: store,
            preferences: preferences,
            onRefresh: { [weak self] in Task { await self?.store.refreshNow() } },
            onOpenSettings: { [weak self] in
                self?.closePanel()
                self?.onOpenSettings()
            }
        )
```

with:

```swift
        let content = DetailPanelView(
            store: store,
            preferences: preferences,
            onRefresh: { [weak self] in Task { await self?.store.manualRefresh() } },
            onOpenSettings: { [weak self] in
                self?.closePanel()
                self?.onOpenSettings()
            },
            onQuit: { [weak self] in
                self?.closePanel()
                self?.onQuit()
            }
        )
```

- [ ] **Step 4: Update `AppDelegate.swift`**

In `Sources/TokenSpendie/AppDelegate.swift`, replace:

```swift
        menuBar = MenuBarController(store: store, preferences: preferences,
                                   onOpenSettings: { [weak self] in self?.showSettings() })
        floatingPanel = FloatingPanelController(store: store, preferences: preferences,
                                               onOpenSettings: { [weak self] in self?.showSettings() })
```

with:

```swift
        menuBar = MenuBarController(store: store, preferences: preferences,
                                   onOpenSettings: { [weak self] in self?.showSettings() },
                                   onQuit: { NSApp.terminate(nil) })
        floatingPanel = FloatingPanelController(store: store, preferences: preferences,
                                               onOpenSettings: { [weak self] in self?.showSettings() },
                                               onQuit: { NSApp.terminate(nil) })
```

- [ ] **Step 5: Build to verify it compiles**

Run: `swift build`
Expected: `Build complete!` with no errors.

- [ ] **Step 6: Run the full test suite**

Run: `swift test`
Expected: PASS — all tests green; nothing regressed.

- [ ] **Step 7: Commit**

```bash
git add Sources/TokenSpendie/UI/DetailPanelView.swift Sources/TokenSpendie/UI/FloatingPanelController.swift Sources/TokenSpendie/UI/MenuBarController.swift Sources/TokenSpendie/AppDelegate.swift
git commit -m "$(cat <<'EOF'
Restructure dropdown: actions section, Quit, hover, refresh spin

Split the cramped footer into a divider-separated actions section
(Settings, Quit) and a status strip. Add hover highlights to the refresh
icon and action rows. The refresh icon spins while a refresh runs and is
disabled then; the button routes through manualRefresh() so it is
throttled. New onQuit closure terminates the app.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Manual QA

After Task 3, run the app (`./build.sh` then launch the built app, or
`swift run`) and verify:

1. **Backdrop** — click the menu bar icon; the icon shows a rounded highlight
   while the dropdown is open, and it clears when the dropdown closes (toggle,
   click-away, and opening Settings).
2. **Actions section** — the dropdown bottom shows `⚙ Settings…` and `⏻ Quit`
   as full-width rows in their own divider-separated section, with the status
   line on its own strip below.
3. **Hover** — hovering the refresh icon and each action row shows a subtle
   rounded highlight that fades in/out.
4. **Refresh animation** — clicking refresh spins the icon while the fetch runs;
   the button is disabled (no second spin) during the fetch.
5. **Anti-spam** — rapidly clicking refresh does not fire repeated requests;
   a click within ~2 s of the last one is ignored.
6. **Quit** — clicking `⏻ Quit` terminates the app.

---

## Self-Review Notes

- **Spec coverage:** Fix 1 (backdrop) → Task 2. Fix 2 (actions section + Quit) →
  Task 3 Steps 1–4. Fix 3 (hover) → Task 3 Step 1 (`RefreshButton`,
  `MenuActionRow`). Fix 4 (refresh animation + anti-spam) → Task 1 + Task 3
  Step 1. All spec testing items → Task 1 Step 2.
- **Type consistency:** `isRefreshing` and `manualRefresh()` defined in Task 1
  are consumed by `RefreshButton` and the controller closures in Task 3.
  `onQuit: () -> Void` is added to `DetailPanelView`, both controllers, and
  supplied by `AppDelegate` — signatures match across all four files.
