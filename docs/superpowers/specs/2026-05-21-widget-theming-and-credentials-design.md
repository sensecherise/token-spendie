# Token Spendie — Theming & Manual Credentials — Design

**Date:** 2026-05-21
**Status:** Approved (pending spec review)

## Overview

Two independent features added to the existing Token Spendie:

- **Feature A — Color theming:** the user picks from preset color themes that
  recolor the menu bar ring and detail-panel bars.
- **Feature B — Manual credential entry:** the user can paste a Claude token
  instead of relying on the Claude Code Keychain item, removing the macOS
  Keychain access prompt.

Both are configured in the existing Preferences window. Multi-provider support
(GPT / Gemini / Copilot) and an API-spend tracker were considered and explicitly
deferred — see Non-goals.

## Feature A — Color theming

### Theme model

A new `Theme` type: `enum Theme: String, CaseIterable`, four cases. Each theme
maps the three usage tiers (calm `< 70%`, warn `70–90%`, hot `> 90%`) to colors:

| Theme | calm | warn | hot |
|---|---|---|---|
| `default` | `#5fb878` | `#e0a23f` | `#d9534f` |
| `ocean` | `#35c0a6` | `#f0bd5a` | `#ef6f6c` |
| `sunset` | `#f0a65e` | `#ec7a55` | `#d94b6e` |
| `violet` | `#6f8fd6` | `#a974d8` | `#d95f9a` |

`Theme` exposes:
- `displayName: String` — for the picker.
- `color(for: UsageLevel) -> Color` — the tier color.

`UsageLevel` keeps `forPercent(_:)`; its hardcoded `color` property is removed —
color now comes from `theme.color(for: level)`. The `default` theme's colors are
exactly the current hardcoded values, so the default appearance is unchanged.

Theming applies to: the menu bar ring, the detail-panel bars, and the bar
percentage text. The bar track and panel background stay neutral system colors.

### Wiring

- `Preferences` gains `@Published var theme: Theme`, UserDefaults-backed,
  default `.default`.
- The menu bar ring is drawn by `MenuBarController.ringImage(...)` (Core
  Graphics — `RingView` was removed when the popover was replaced). Theming the
  ring means `refreshButton()` reads `preferences.theme` and passes the tier
  color into `ringImage`.
- `MenuBarController` gains a `preferences` reference and re-renders the button
  on `preferences.objectWillChange` (alongside its existing `store` observation).
- `DetailPanelView` observes `Preferences` and passes the resolved `theme` down
  to `UsageBarRow` (a leaf view taking a plain `Theme` value).
- `AppDelegate` already builds `Preferences`; it now also passes it to
  `MenuBarController`.

Containers read `preferences.theme`; leaf views take plain values. A theme
change recolors the ring and panel immediately.

### Preferences UI

A new "APPEARANCE" section in `PreferencesView` with a theme picker: a row of
four selectable swatch-trios, each showing the theme's three tier colors and its
name. Selecting one applies it live.

### Persistence & edge cases

- Stored in `UserDefaults` under key `theme` as the raw string. An unset or
  unrecognized value falls back to `.default`.
- Theme colors are fixed RGB chosen to read on both the (dark) menu bar and the
  detail panel. There are no separate light/dark variants.

## Feature B — Manual credential entry

### Sources & model

The widget fetches usage with an OAuth bearer token. Today that token is read
from Claude Code's Keychain item, which triggers a macOS Keychain access prompt.
This feature adds a second credential source behind the existing
`CredentialStore` protocol:

- `KeychainReader` (existing) — reads Claude Code's `Claude Code-credentials`
  Keychain item. This is **Auto** mode. It prompts once per app version
  (the user clicks "Always Allow").
- `ManualTokenStore` (new) — returns a token the user pasted, stored in the
  app's **own** Keychain item, service `com.cherise.TokenSpendie.token`. Because
  the app created that item, reading it never triggers a prompt.

`Preferences` gains `credentialMode: CredentialMode` (`.auto` / `.manual`),
default `.auto`. `UsageStore` uses the `CredentialStore` that the current mode
selects.

### What the user pastes

A long-lived Claude **OAuth token**, obtained by running `claude setup-token` in
Terminal and copying the result. This token carries the user's subscription
identity and works with the `/api/oauth/usage` endpoint.

It is **not** an `sk-ant-` console API key. Console API keys authenticate the
pay-per-token API, not the subscription usage endpoint, and will not work here.
The Preferences UI states this.

> Implementation note: that a `claude setup-token` token is accepted by
> `GET /api/oauth/usage` must be verified as the first step of implementation
> (the same way the original endpoint was verified by a probe).

### Preferences UI

A new "CREDENTIAL" section in `PreferencesView`:

- A mode picker: **Auto (Claude Code Keychain)** / **Manual token**.
- In Manual mode: a secure text field to paste the token, a **Save** button, and
  help text — "Run `claude setup-token` in Terminal, then paste the token here."
- A status line reflecting the current credential: working, or the relevant
  error.

### Behavior & edge cases

- **Auto mode:** `UsageStore` uses `KeychainReader` (current behavior).
- **Manual mode:** `UsageStore` uses `ManualTokenStore`. No Keychain prompt.
- **Switching mode:** `UsageStore` swaps its `CredentialStore` and refreshes.
- **Manual mode, no token saved:** the widget shows an error state — "No token —
  add one in Settings."
- **Invalid or expired pasted token:** surfaces through the existing
  `loginExpired` / `badResponse` states; the user re-pastes a fresh token.

## Non-goals

- Multi-provider usage (GPT / Gemini / Copilot). Those providers expose no
  per-user usage comparable to the Claude Code session/weekly model — see the
  earlier brainstorming. Deferred.
- An API-spend tracker (month-to-date `$` via admin keys for OpenAI / Anthropic
  API). A separate feature with a different metaphor; deferred to its own spec.
- Free-form color picking. Themes are fixed presets by choice.
- Moving the Keychain read off the main thread. `SecItemCopyMatching` runs on
  the main actor and can block it while the prompt is open; manual mode avoids
  the prompt entirely, so this is noted but not addressed here.

## Testing

- `Theme.color(for:)` returns the correct color for each tier; `Theme(rawValue:)`
  round-trips; `Preferences.theme` and `Preferences.credentialMode` persist
  across instances (extend `PreferencesTests`).
- `ManualTokenStore` save/load round-trip, and a "no token saved" path that
  surfaces the correct error.
- `UsageStore` uses the correct `CredentialStore` for each mode and refreshes on
  a mode switch.
- UI recolor and the new Preferences sections are build-verified and checked
  manually.

## Risks

1. **`claude setup-token` compatibility** — whether that token is accepted by
   `GET /api/oauth/usage` must be confirmed before the manual path is built.
2. **Storing a token in the app's Keychain** — the app writes and reads its own
   generic-password item; standard `Security` framework usage, no prompt, but
   the write path needs the same care as the read path.
