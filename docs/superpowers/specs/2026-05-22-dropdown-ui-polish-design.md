# Dropdown UI Polish — Design

Date: 2026-05-22
Branch: `dropdown-ui-polish`

## Goal

Four polish fixes to the menu bar dropdown so it behaves like a native macOS
menu-bar app:

1. Highlight the status item while the dropdown is open (backdrop).
2. Give the action buttons their own divider-separated section, and add a Quit
   action the widget currently lacks.
3. Add hover states to the refresh icon and the action rows.
4. Animate the refresh icon while a refresh runs, and make the refresh button
   un-spammable.

No behavior outside the dropdown changes. Polling, theming, and credentials are
untouched.

## Fix 1 — Status item backdrop

The widget uses a custom borderless `NSPanel` for its dropdown instead of a real
`NSMenu`, so the status item never shows the native "selected" backdrop. Real
menu-bar apps get it for free.

**Change:** `MenuBarController`
- `openPanel()` — call `statusItem?.button?.highlight(true)` after the panel is
  shown.
- `closePanel()` — call `statusItem?.button?.highlight(false)`.

`NSStatusBarButton.highlight(_:)` draws the standard rounded selection backdrop.
The highlight must be cleared in every close path (outside-click monitor,
toggle, settings open) — `closePanel()` is the single funnel for all of them, so
clearing it there covers every case.

## Fix 2 — Settings actions section + Quit

`DetailPanelView` today ends in one cramped footer row: small status text on the
left, a tiny `⚙ Settings` text button on the right.

**New bottom structure** (top to bottom):
- existing content
- `Divider`
- **actions section** — full-width rows, one per action:
  - `⚙ Settings…` → existing `onOpenSettings`
  - `⏻ Quit` → new `onQuit`
- `Divider`
- **status strip** — the `updated 2m ago` / `offline — …` line alone, small and
  secondary, full-width.

**New closure `onQuit`:** `DetailPanelView` gains an `onQuit: () -> Void`
property. It is supplied by both panel hosts:
- `MenuBarController.openPanel()` — `onQuit` closes the panel, then quits.
- `FloatingPanelController.show()` — `onQuit` quits.
- `AppDelegate` provides the quit implementation: `NSApp.terminate(nil)`, passed
  into both controllers' initializers as `onQuit: @escaping () -> Void`,
  mirroring the existing `onOpenSettings` wiring.

**New view `MenuActionRow`:** a reusable row — leading SF Symbol icon, label,
full-width tap target, hover background (see Fix 3). Used for both Settings and
Quit. Lives in `DetailPanelView.swift`.

The refresh control stays in the header (top-right icon). It is not moved into
the actions section.

## Fix 3 — Hover states

Subtle translucent highlight, no accent fill. Adapts to light/dark because it is
expressed as an opacity over `Color.primary`.

- **`MenuActionRow`:** `@State private var hovering`, `.onHover { hovering = $0 }`.
  When hovering, the row paints a background of `Color.primary.opacity(0.09)`
  behind its full width, with a small rounded rectangle inset (≈6 pt radius,
  ≈5 pt horizontal inset) so it reads as a menu highlight.
- **Refresh icon button:** same `.onHover` pattern; hovering paints a rounded
  background `Color.primary.opacity(0.12)` sized to the icon's tap area
  (≈20×20 pt, ≈6 pt radius).

A short `.animation(.easeOut(duration: 0.12), value: hovering)` smooths the
fade.

## Fix 4 — Refresh animation + anti-spam

### Store change — `UsageStore`

- New `@Published private(set) var isRefreshing: Bool = false`.
- `refreshNow()` sets it: `isRefreshing = true` at the top of the body, and a
  `defer { isRefreshing = false }` so it clears on every exit path (success,
  error, early `backoffUntil` return). The early `backoffUntil` return at the
  top runs before the flag is set, so it stays `false` in that case — correct,
  no work happened.
- New `manualRefresh()` method, the only entry point the refresh button calls:
  - Track `private var lastManualRefresh: Date?`.
  - No-op if `isRefreshing` is already `true`.
  - No-op if `lastManualRefresh` is set and `now() - lastManualRefresh < 2`
    seconds.
  - Otherwise set `lastManualRefresh = now()` and `await refreshNow()`.
- The 429 `backoffUntil` guard inside `refreshNow()` still applies, so a manual
  refresh during rate-limit backoff still no-ops.

### Button behavior — refresh icon in `DetailPanelView` header

- Action calls a new `onRefresh` that routes to `store.manualRefresh()` (today
  it calls `store.refreshNow()` directly — both `MenuBarController` and
  `FloatingPanelController` update their closures).
- Spin: `.rotationEffect` driven by a continuous linear animation that repeats
  forever while `store.isRefreshing` is `true`, and stops when it returns to
  `false`. A sub-second refresh produces a brief partial spin — acceptable.
- Disabled while `store.isRefreshing` is `true`.

### Anti-spam summary

Two layers: (1) the button is disabled while a refresh is in flight; (2)
`manualRefresh()` silently no-ops if the last manual refresh was under 2 seconds
ago. During the 2 s cooldown the button is clickable but does nothing and does
not spin — no extra dimming state. Timer-driven, wake, network-reconnect, and
panel-open refreshes are unaffected; they still call `refreshNow()` directly and
are not rate-limited by the manual gap (but they do animate the icon, since they
set `isRefreshing`).

## Files touched

| File | Change |
|------|--------|
| `Sources/TokenSpendie/UI/MenuBarController.swift` | Highlight status item on open/close; pass `onQuit`; route refresh to `manualRefresh()` |
| `Sources/TokenSpendie/UI/DetailPanelView.swift` | Actions section + status strip; new `MenuActionRow`; `onQuit` property; refresh icon hover + spin + disable |
| `Sources/TokenSpendie/UI/FloatingPanelController.swift` | Pass `onQuit`; route refresh to `manualRefresh()` |
| `Sources/TokenSpendie/AppDelegate.swift` | Provide `onQuit` (`NSApp.terminate`) to both controllers |
| `Sources/TokenSpendie/Store/UsageStore.swift` | `isRefreshing` published flag; `manualRefresh()` with 2 s gap |
| `Tests/TokenSpendieTests/UsageStoreTests.swift` | Cover `manualRefresh()` gap + `isRefreshing` lifecycle |

## Testing

- **`manualRefresh()` gap:** two `manualRefresh()` calls within 2 s → provider
  called once; a third after the gap → provider called again.
- **`isRefreshing` lifecycle:** `false` before, `true` during, `false` after a
  `refreshNow()`; clears even when the fetch throws.
- **Backoff interaction:** `manualRefresh()` while `backoffUntil` is in the
  future → no provider call.
- Hover, spin, status-item highlight, and the section layout are visual — verified
  by running the app, not unit-tested.

## Out of scope

- No change to the floating panel layout beyond the shared `DetailPanelView`
  edits (it reuses the same view, so it gets the section, hover, and Quit for
  free).
- No accent-fill hover style.
- No keyboard shortcuts for the new rows.
