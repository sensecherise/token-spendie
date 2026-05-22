# Multi-CLI Usage Display — Design

**Date:** 2026-05-22
**Branch:** `feature/multi-cli-usage` (to be created)
**Status:** Approved

## Goal

Extend Token Spendie from Claude-only to multiple AI CLIs. The detail panel
shows every detected CLI's usage; the menu bar shows one CLI's ring at a time,
which the user picks. Phase 1 ships **Claude** (full support) and **Gemini**
(best-effort). The architecture is left pluggable so Codex and Copilot can be
added later with no UI change.

## Current state

- **Single provider.** `UsageProvider` is a protocol with one method,
  `fetchUsage(accessToken:)`, and one conformer, `EndpointUsageProvider`, which
  hits Claude's `/api/oauth/usage`.
- `UsageStore` owns one global `LoadState`, one `UsageSnapshot`, and is injected
  with a single `CredentialStore`. It performs the 401 retry (re-read Keychain)
  itself.
- `MenuBarController` draws the session ring + `" 47%"` into the status button.
- `DetailPanelView` renders header + usage rows (Session / Weekly / per-model)
  + actions section.
- `UsageSnapshot` = `session`, `weekly`, `modelWeeklies`, `fetchedAt`.
  `UsageWindow` = `percent`, `resetsAt`. `ModelWeekly` = `model`, `window`.

## Scope

**In:** Claude (full), Gemini (best-effort), multi-provider panel, menu-bar
ring picker, credential auto-detection, best-effort plan label.

**Out:** Codex and Copilot — neither exposes individual usage via a usable API
(see Verified constraints). Deferred; the protocol leaves room. Also out:
notifications/alerts, per-provider refresh intervals, a Settings provider
filter.

## Verified constraints (research, 2026-05-22)

- **Claude** — `/api/oauth/usage` is reliable (already shipping). A plan/tier
  field may be present in the payload but is unverified → treat plan as
  best-effort.
- **Gemini** — `POST https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota`
  returns per-model quota buckets (`modelId`, `tokenType` incl. `REQUESTS`,
  `remainingFraction`, `resetTime`). It is an internal "Cloud Code Private API":
  fragile, 403-prone, and works only on the OAuth login path (not API-key).
  Tier is available via `loadCodeAssist` (`currentTier`).
- **Codex / Copilot** — no individual usage API. Codex usage lives behind
  OpenAI internal endpoints (open feature request `openai/codex#15281`);
  GitHub does not expose individual Copilot premium-request usage via API.
  Deferred.

## Design

### Data model

```
enum ProviderID: String, Codable { case claude, gemini }   // extensible

struct UsageWindow {            // unchanged
    let percent: Double
    let resetsAt: Date?
}

struct LabeledWindow {          // a window plus how to title it in the panel
    let label: String           // e.g. "Session · 5h", "Pro · requests"
    let resetLine: String       // e.g. "5-hour window · resets in 2h 10m"
    let window: UsageWindow
}

struct ProviderSnapshot {
    let id: ProviderID
    let displayName: String     // "Claude", "Gemini"
    let plan: String?           // best-effort; nil hides the pill
    let headline: LabeledWindow // drives the ring + collapsed row %
    let windows: [LabeledWindow]// complete native list, shown on expand;
                                // includes the headline window
    let fetchedAt: Date
}

enum ProviderState {
    case loading                // no snapshot yet
    case ok                     // fresh snapshot
    case stale                  // cached snapshot, last refresh failed
    case error(UsageError)      // no usable snapshot
}

struct ProviderUsage {          // one panel row's complete state
    let id: ProviderID
    let displayName: String
    var state: ProviderState
    var snapshot: ProviderSnapshot?
}
```

The **headline** is the provider's most-constraining live window. Claude →
Session (`five_hour`). Gemini → the model `REQUESTS` bucket with the highest
used percent. Each provider picks its own headline when building its snapshot.

### UsageProvider protocol

```
protocol UsageProvider {
    var id: ProviderID { get }
    var displayName: String { get }
    func detectCredentials() -> Bool          // are this CLI's creds present?
    func fetchUsage() async throws -> ProviderSnapshot
}
```

Credential handling moves **into each provider**. `ClaudeProvider` owns the
Keychain read and the 401-retry (re-read Keychain once); `GeminiProvider` owns
the OAuth-creds-file read. `UsageStore` no longer holds a shared
`CredentialStore` and no longer performs the 401 retry. Each provider throws
the existing `ProviderError` / `CredentialError` cases, which the store maps to
`ProviderState` per row.

### Detection

Each refresh cycle, the store calls `detectCredentials()` on every registered
provider. Providers that return `true` get a row, in a fixed priority order:
Claude, then Gemini. A provider whose creds vanish (logout) drops its row on
the next cycle.

### UsageStore — multi-provider

- `@Published private(set) var providers: [ProviderUsage]` — one entry per
  **detected** provider, each with its own `state` and `snapshot`.
- One refresh cycle fans out: detect, then fetch all detected providers
  concurrently (task group / `async let`). One provider throwing never aborts
  the others — each result is applied to its own `ProviderUsage`.
- `SnapshotCache` is keyed by `ProviderID` so each provider caches and restores
  independently.
- `@Published private(set) var menuBarProviderID: ProviderID` — the resolved
  provider shown in the menu bar. `setMenuBarProvider(_:)` changes it and
  persists to `Preferences`.
- Polling interval, wake/network observers, and 429 backoff stay global but now
  apply per provider where relevant (429 backoff is tracked per provider id).

### Menu bar

`MenuBarController.refreshButton()` reads the active provider via
`store.menuBarProviderID` and draws its `headline` ring + percent. When more
than one provider is detected, a small provider glyph is prefixed so the user
knows which CLI the ring belongs to (e.g. `✳ 47%` / `✦ 32%`); with only one
provider the glyph is omitted — identical to today, so single-CLI users see no
change.

Fallback: if the active provider is errored or no longer detected, the menu bar
falls back to the next detected provider with a usable snapshot. If none, the
existing error/empty glyphs (`✳ –`, `✳ !`, `✳ …`) are shown.

### Panel

`DetailPanelView` is restructured around a new `ProviderRow` view:

- **Header** — unchanged (`TOKEN SPENDIE` + `RefreshIndicator`).
- **Body** — `ForEach(store.providers)` → `ProviderRow`.
- **`ProviderRow` collapsed** — badge glyph, display name, plan pill
  (`plan` non-nil), headline percent, headline ring, chevron.
- **`ProviderRow` expanded** — the provider's `windows` rendered with the
  existing `UsageBarRow` (reused unchanged: `title` ← `label`, `resetLine` ←
  `resetLine`, `window`, `theme`).
- **Accordion** — `@State expandedProviderID: ProviderID?`; tapping a row's
  body expands it and collapses any other. When exactly one provider is
  detected its row is expanded by default and the chevron is hidden.
- **Ring picker** — tapping a row's ring calls `store.setMenuBarProvider(id)`.
  The active provider's ring shows a blue glow + the row gets an accent
  treatment. Only providers in `.ok` / `.stale` (i.e. with a headline) are
  ring-eligible; an errored/loading row has no tappable ring. The ring and the
  row body are distinct tap targets.
- **Per-row state** — `.ok` renders normally; `.stale` dims the row and shows
  `stale · Xm`; `.error` shows the mapped message (the existing
  `messageView(for:)` strings, scoped to the row) with a tappable fix where one
  exists; `.loading` shows `fetching…`.
- **Empty state** — no detected providers → the existing `🔌` message,
  generalized: "No AI CLIs detected. Log in to Claude Code or Gemini CLI and
  they appear here automatically."
- **Actions** — unchanged (Settings…, Quit).

### Gemini provider specifics

- **Detection** — Gemini CLI's OAuth credentials file (path confirmed at
  implementation time; expected under `~/.gemini/`). API-key-only setups have no
  OAuth creds and are simply not detected — acceptable for best-effort.
- **Fetch** — `POST .../v1internal:retrieveUserQuota` with the OAuth access
  token. For each `REQUESTS` bucket: `percent = (1 - remainingFraction) * 100`,
  `resetsAt` ← `resetTime`. `headline` = the bucket with the highest percent.
- **Plan** — best-effort via `loadCodeAssist` `currentTier`; on failure the
  plan pill is simply omitted.
- **Failure isolation** — any 403, incomplete payload, or network error puts
  only the Gemini row into `.error` / `.stale`. Claude is never affected.

### Preferences

Add `menuBarProviderID: ProviderID`, persisted. Default = the first detected
provider by priority order. No other new preferences. Refresh interval stays
global.

## Out of scope

- Codex and Copilot providers (deferred; the protocol leaves room).
- Usage notifications or high-usage alerts.
- Per-provider refresh intervals or per-provider visibility toggles.
- Floating always-on-top panel changes beyond rendering the new rows.

## Files touched

- `Model/UsageModels.swift` — add `ProviderID`, `LabeledWindow`,
  `ProviderSnapshot`, `ProviderState`, `ProviderUsage`.
- `Data/UsageProvider.swift` — generalize the protocol (`id`, `displayName`,
  `detectCredentials()`, `fetchUsage()` with self-owned auth).
- `Data/EndpointUsageProvider.swift` → `ClaudeProvider` — absorb the Keychain
  read and 401-retry; build a `ProviderSnapshot` (headline = Session).
- `Data/GeminiProvider.swift` — **new** — detection, `retrieveUserQuota` fetch,
  best-effort tier.
- `Data/UsageDecoder.swift` — keep the Claude decoder; add a Gemini decoder
  (here or a new `GeminiDecoder.swift`).
- `Store/UsageStore.swift` — multi-provider state, concurrent per-provider
  polling, per-provider 429 backoff, `menuBarProviderID`.
- `Store/SnapshotCache.swift` — key cache entries by `ProviderID`.
- `Store/Preferences.swift` — add `menuBarProviderID`.
- `UI/MenuBarController.swift` — draw the active provider's badge + ring + %,
  with fallback.
- `UI/DetailPanelView.swift` — `ProviderRow`, accordion, ring picker, empty
  state; reuse `UsageBarRow`.
- `Tests/TokenSpendieTests/` — new and updated tests (see Testing).

## Testing

- **Unit**
  - Headline selection: Claude → Session; Gemini → highest-used `REQUESTS`
    bucket.
  - Gemini decoder against a sample `retrieveUserQuota` payload (including the
    known incomplete-payload case — only a `REQUESTS` bucket present).
  - Detection: provider included only when `detectCredentials()` is true.
  - Menu-bar fallback when the active provider errors or stops being detected.
  - Accordion: expanding one row collapses the previously expanded one.
  - Per-provider error isolation: a Gemini fetch failure leaves Claude `.ok`.
- **Manual**
  - Claude-only: no regression — single row, expanded, no chevron, menu bar
    unchanged.
  - Both detected: two rows, accordion expand, plan pills, headline rings.
  - Tap a ring → menu bar switches CLI; blue glow follows.
  - Force a Gemini 403 → Gemini row goes `stale`/`error`; Claude unaffected.
  - No CLIs → `🔌` empty state.

## Phasing

Implementation lands in two reviewable steps:

1. **Refactor to the provider-array architecture with Claude only** — new
   models, generalized protocol, `ClaudeProvider`, multi-provider `UsageStore`,
   `ProviderRow` panel. No user-visible change (one provider, behaves like
   today).
2. **Add Gemini** — `GeminiProvider`, decoder, detection, and the multi-row /
   ring-picker behaviors that only become visible with a second provider.
