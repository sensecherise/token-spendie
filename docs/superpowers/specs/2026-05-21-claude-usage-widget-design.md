# Claude Usage Widget — Design

**Date:** 2026-05-21
**Status:** Approved (pending spec review)

## Overview

A native macOS menu bar app that displays Claude Code subscription usage in
real time: the rolling 5-hour **session** window and the **weekly** cap, plus a
separate **Opus weekly** cap when the plan reports one. The numbers are the
official figures — the same data shown by Claude Code's `/usage` screen.

The app runs as a background accessory (no Dock icon). The usage display is
available as a menu bar item, an optional floating desktop panel, or both — the
user chooses in Preferences.

Intended for personal use and for sharing with friends as a single `.app`.

## Goals

- Always-visible, glanceable session usage in the menu bar.
- Full breakdown (session, weekly, Opus) in a detail panel.
- Near-real-time updates without hammering the data source.
- Exact official numbers, matching Claude Code's `/usage`.
- Distributable as a single self-contained `.app` with no runtime dependencies.

## Non-goals (v1)

- Usage history or graphs over time.
- Notifications when approaching a cap.
- Cross-platform support (macOS only).
- Notarized / App Store distribution (unsigned sharing only).

## Data source

The widget reads the OAuth token Claude Code already stores, then queries the
Anthropic usage data with it.

- **Token:** the `Claude Code-credentials` generic-password item in the user's
  login Keychain. The stored value is a JSON blob containing the OAuth access
  token, refresh token, and expiry. Exact field names must be confirmed during
  implementation (see Risks).
- **Primary fetch — dedicated usage endpoint (Approach 1):** an authenticated
  request to the Anthropic usage endpoint that backs Claude Code's `/usage`
  screen, returning structured data (session %, weekly %, reset times). One
  GET, rich JSON, consumes no usage quota.
- **Fallback fetch — rate-limit headers (Approach 2):** send a minimal API
  request and read the `anthropic-ratelimit-unified-*` response headers. More
  stable surface, coarser data, consumes a sliver of quota per poll.

The fetch strategy sits behind a `UsageProvider` protocol, so switching from
Approach 1 to Approach 2 is a one-object swap.

## Architecture

A single Swift package producing one executable, assembled into an
`LSUIElement` accessory `.app`. Internally split into focused units:

| Component | Responsibility | Depends on |
|---|---|---|
| `KeychainReader` | Read `Claude Code-credentials` from the login Keychain; parse the JSON blob; return access token, refresh token, expiry. | Security framework |
| `UsageProvider` (protocol) | `fetchUsage() async throws -> UsageSnapshot`. | — |
| `EndpointUsageProvider` | Approach 1 — calls the dedicated usage endpoint. | `UsageProvider`, URLSession |
| `HeaderUsageProvider` | Approach 2 fallback — reads rate-limit response headers. | `UsageProvider`, URLSession |
| `UsageSnapshot` | Immutable data model: session %/reset, weekly %/reset, optional Opus-weekly %/reset, `fetchedAt`. | — |
| `UsageStore` | `ObservableObject`. Owns the refresh timer, calls the provider, publishes the latest snapshot + status (`loading`/`ok`/`error`/`stale`). Single source of truth. | `UsageProvider`, `KeychainReader` |
| `MenuBarController` | Owns the `NSStatusItem` (draws the session ring) and the click-to-open detail popover. | `UsageStore` |
| `FloatingPanelController` | Owns an always-on-top, draggable `NSPanel` hosting the same detail view. | `UsageStore` |
| `PreferencesView` | Toggles: menu bar item, floating panel, refresh interval, launch-at-login. | `UsageStore`, ServiceManagement |
| `AppDelegate` | Wires components together; configures the accessory app. | all |

Both UIs render from one `UsageStore`, so they can never disagree. The menu bar
item and floating panel are independently toggleable (on, off, or both).

## UI design

**Menu bar item.** A circular progress ring for the **session** percentage with
the number beside it (e.g. `◐ 52%`). Click opens the detail popover. The ring
is color-coded (see Color scale).

**Detail panel** (shared by the click-popover and the floating panel):

- Header: `CLAUDE USAGE` title + a refresh button.
- Three stacked metric rows — **Session**, **Weekly**, **Weekly · Opus** — each
  a labelled horizontal progress bar with its percentage and a reset line
  (e.g. "5-hour window · resets in 2h 47m", "all models · resets Mon, May 25").
- Footer: "updated 14s ago" + a Settings entry.
- The Opus row renders only if the data source reports a separate Opus cap.

**Floating panel.** The same detail view hosted in an always-on-top, draggable
`NSPanel`. Toggled in Preferences.

**Color scale** (applied to ring and bars):

| Range | Meaning | Color |
|---|---|---|
| < 70% | calm | green `#5fb878` |
| 70–90% | getting close | amber `#e0a23f` |
| > 90% | near cap | red `#d9534f` |

## Data flow & refresh behavior

- **On launch:** the last snapshot is loaded from an on-disk cache and shown
  immediately (no blank flash); then `KeychainReader` → token → provider fetch
  → `UsageStore` publishes the fresh snapshot.
- **Polling:** a timer refreshes every **60 s** by default. Off-schedule
  refreshes trigger on: the popover/floating panel being open (interval drops
  to 20 s while visible), a manual refresh-button click, and system wake /
  network reconnect.
- **Preferences interval:** 30 s / 60 s / 2 min.
- **Token expiry:** on a `401`, re-read the Keychain first — Claude Code
  refreshes the token during normal use, so the current one is usually already
  there. If still expired, show the `login expired` state. The widget never
  writes back to Claude Code's Keychain item.
- **Staleness:** if the last *successful* fetch is older than 3× the configured
  refresh interval, the snapshot is marked `stale` — the UI dims and the footer
  shows the age, so a frozen number is never mistaken for a live one.

## Error handling & states

The menu bar item is always one of: **live ring**, **dimmed grey ring**
(stale), **`✳ –`** (not set up), or **`✳ !`** (needs attention).

| Situation | Menu bar | Panel message |
|---|---|---|
| Live — normal | colored ring + % | session / weekly / Opus bars |
| Offline / endpoint unreachable | dimmed grey ring | last cached values, dimmed; "offline — updated 12m ago" |
| Claude Code not installed / never logged in (no Keychain item) | `✳ –` | "Claude Code not found. Install and log in to Claude Code." |
| Keychain access denied by user | `✳ !` | "Keychain access needed. Click to retry and choose Allow." |
| Token expired, re-read didn't help | `✳ !` | "Login expired. Run any Claude Code command to refresh." |
| Endpoint returned an unexpected shape | `✳ !` | "Couldn't read usage data. The widget will keep retrying." |
| Plan has no separate Opus cap | (normal) | Opus row omitted |

Transient network blips never produce error noise — they fall back to the
cached snapshot marked stale. A persistent "couldn't read usage data" state is
the signal to switch in the `HeaderUsageProvider` fallback.

## Preferences

- Show menu bar item — on / off.
- Show floating panel — on / off.
- Refresh interval — 30 s / 60 s / 2 min.
- Launch at login — on / off (via `SMAppService`).

Preferences are persisted in `UserDefaults`. At least one display surface
(menu bar or floating panel) should remain enabled.

## Build, packaging & sharing

- **Package:** one Swift package, single executable target. System frameworks
  only — AppKit, SwiftUI, Security, ServiceManagement, Foundation. No
  third-party dependencies.
- **Deployment target:** macOS 13 Ventura or later (required by
  `SMAppService`).
- **Build:** `build.sh` runs `swift build -c release`, then assembles
  `ClaudeUsage.app` — `Contents/MacOS/` (binary), `Contents/Info.plist`
  (`LSUIElement = true`, bundle id, version), `Contents/Resources/AppIcon.icns`.
  A simple app icon is generated as part of the build.
- **Launch at login:** `SMAppService.mainApp`.
- **Sharing:** `build.sh` zips the finished `.app`. A short `README.md` covers
  the three things a friend needs:
  1. Claude Code installed and logged in.
  2. First launch: right-click the app → **Open** (clears the Gatekeeper
     warning for an unsigned app).
  3. Choose **Allow** on the Keychain access prompt.

## Testing

- **Unit tests** for pure logic: `UsageSnapshot` JSON decoding, percent→color
  mapping, stale detection, reset-time formatting ("resets in 2h 47m").
- The `UsageProvider` and `KeychainReader` protocol seams let `UsageStore` be
  tested with mock providers — no real network or Keychain in tests.
- The UI layer is kept thin and verified manually.

## Risks & open questions

These are resolved as the **first steps of implementation**, before UI work:

1. **Usage endpoint URL and response schema** — Approach 1 depends on an
   internal/undocumented endpoint. Step 1 is to verify the exact URL and JSON
   shape (and whether it returns the Opus cap). If it cannot be verified or is
   unreliable, fall back to Approach 2.
2. **Keychain blob format** — confirm the JSON field names for the access
   token, refresh token, and expiry inside the `Claude Code-credentials` item.
3. **Token refresh behavior** — confirm that Claude Code refreshes the shared
   Keychain token frequently enough that the re-read-on-401 strategy is
   sufficient in practice.

## Implementation order (high level)

1. Verify the data source (endpoint + Keychain blob format).
2. `KeychainReader` + `UsageProvider` + `UsageSnapshot` + `UsageStore`, with
   unit tests against mocks.
3. `MenuBarController` with the session ring and detail popover.
4. `FloatingPanelController`.
5. `PreferencesView` + launch-at-login.
6. Error/empty states.
7. `build.sh`, app icon, `README.md`, and packaging.
