# Codex + Antigravity Providers — Design

**Date:** 2026-06-12
**Branches:** `feature/codex-provider` (phase 1), `feature/antigravity-provider` (phase 2)
**Status:** Approved

## Goal

Add OpenAI Codex and Google Antigravity usage display to Token Spendie on both
macOS and Windows. Both plug into the existing multi-provider architecture
(`UsageProvider` protocol / `IUsageProvider` interface, provider arrays,
`ProviderRow` panel) shipped with the multi-CLI work — no structural UI change.

Codex gets **full support** (credentials file + usage endpoint + token
refresh), the same tier as Claude. Antigravity gets **best-effort support** via
a local language-server probe: live data while the Antigravity IDE or `agy`
CLI is running, cached/stale data otherwise.

## Current state

- Provider arrays: `AppDelegate.swift:20` (`[ClaudeProvider(), GeminiProvider()]`)
  and `App.xaml.cs:42` (`new IUsageProvider[] { ClaudeProvider, GeminiProvider }`).
- `ProviderID` enums: `claude`, `gemini` (Swift `Model/UsageModels.swift:51`,
  C# `Models/ProviderID.cs`).
- Panel rows render purely from `ProviderUsage.displayName` + snapshot;
  menu bar / tray draws the active provider's headline ring + percent.
- `SnapshotCache` is keyed by `ProviderID`; per-provider 429 backoff and error
  isolation already exist.
- Windows widget-board cards (`TokenSpendie.WidgetProvider`) fetch one
  snapshot via `SnapshotFetcher` with a Claude → Gemini fallthrough.
- The 2026-05-22 multi-CLI spec deferred Codex because no usage API existed
  then. That constraint no longer holds (see below).

## Verified constraints (research, 2026-06-12)

Primary reference: [CodexBar](https://github.com/steipete/codexbar) (MIT),
which ships both integrations in production; its `docs/codex-oauth.md` and
`docs/antigravity.md` document the protocols, cross-checked against its Swift
sources (cloned 2026-06-12).

### Codex

- Credentials: `~/.codex/auth.json` (`$CODEX_HOME/auth.json` when set;
  `%USERPROFILE%\.codex\auth.json` on Windows), written by Codex CLI on login:
  `{ "OPENAI_API_KEY": …, "tokens": { "id_token", "access_token",
  "refresh_token", "account_id" }, "last_refresh": ISO-8601 }`.
- Usage: `GET https://chatgpt.com/backend-api/wham/usage` with
  `Authorization: Bearer <access_token>` and `ChatGPT-Account-Id: <account_id>`.
  Response: `plan_type` (string), `rate_limit.primary_window` and
  `.secondary_window` — each `{ used_percent, reset_at (epoch seconds),
  limit_window_seconds }` (primary ≈ 5 h session, secondary ≈ weekly) — and
  `credits { has_credits, unlimited, balance }`. Any window may be absent.
- Token refresh: `POST https://auth.openai.com/oauth/token` with
  `{ client_id: "app_EMoamEEZ73f0CkXaXp7hrann", grant_type: "refresh_token",
  refresh_token, scope: "openid profile email" }`. Codex CLI's own policy is
  to refresh when `last_refresh` is older than 8 days. Refresh tokens rotate:
  the response carries a new `refresh_token`, and reusing an old one can be
  rejected (`refresh_token_reused`), so a refreshed token set **must** be
  written back to `auth.json` or the CLI's stored login degrades.

### Antigravity

- No reusable on-disk OAuth credentials. The remote-quota path requires running
  a Google OAuth login flow with Antigravity's own OAuth client (extracted from
  the app binary) — rejected: Token Spendie never logs users in.
- Local path: the Antigravity IDE and the `agy` CLI both run a Codeium-style
  language server that exposes a local Connect-RPC HTTP API:
  - Process match: IDE language server (`language_server_*` binary with
    Antigravity markers: `--app_data_dir antigravity` or `/antigravity/` in
    path) requires a `--csrf_token <token>` argument; the CLI
    (`agy` / `antigravity-cli` path segment) exposes no CSRF flag and
    requires none.
  - The server listens on localhost ports with a **self-signed TLS cert**;
    HTTP fallback exists on the IDE's `--extension_server_port`.
  - Probe: `POST https://127.0.0.1:<port>/exa.language_server_pb.LanguageServerService/GetUnleashData`
    with headers `X-Codeium-Csrf-Token: <token>`,
    `Connect-Protocol-Version: 1`; first 200 selects the port.
  - Quota: `POST …/GetUserStatus` (fallback `…/GetCommandModelConfigs`) with a
    minimal metadata body (`ideName: antigravity`, `extensionName:
    antigravity`, `locale: en`, `ideVersion: unknown`). Per-model quota at
    `userStatus.cascadeModelConfigData.clientModelConfigs[].quotaInfo`
    (`remainingFraction`, `resetTime` — ISO-8601 or epoch seconds).
    `planName` / `accountEmail` only from `GetUserStatus`.
- Internal protocol; fields may change. This is why Antigravity is
  **best-effort**, like Gemini.

### Local machine (for manual testing)

`agy` CLI installed (`~/.local/bin/agy`, state in `~/.gemini/antigravity-cli/`);
no Antigravity IDE; no `~/.codex` yet — Codex manual testing needs a
`codex login` first, unit tests run from sanitized fixtures.

## Scope

**In:** Codex provider (macOS + Windows, full support), Antigravity provider
(macOS + Windows, best-effort probe), Codex in the Windows widget-board
fallthrough, new `ProviderID` cases, per-provider error messages, tests.

**Out:** own login flows of any kind, browser-cookie/web-dashboard scraping,
`codex app-server` RPC fallback, Antigravity in the widget-board COM server
(no process probing in that process), usage notifications, Codex credits
display (the `credits` field is ignored by the decoder — YAGNI until someone
asks), Copilot.

## Design

### Codex provider

`CodexProvider` (Swift) / `CodexProvider` (C#), `id = .codex`,
`displayName = "Codex"`.

**Detection.** `auth.json` exists at the resolved path and contains a
non-empty `tokens.access_token`. API-key-only files (no `tokens` object) are
not detected — same policy as Gemini's API-key-only setups. `$CODEX_HOME`
respected on both platforms.

**Fetch.**
1. Load credentials from `auth.json`.
2. If `last_refresh` is older than 8 days → refresh first (below).
3. `GET …/wham/usage` with the headers above plus
   `User-Agent: TokenSpendie/1.0`.
4. `401` → refresh once, write back, retry once. A second `401` → credential
   error row: "Run `codex` to re-authenticate."
5. `429` → existing per-provider backoff. Other non-200 → provider error.

**Refresh + write-back.** `POST auth.openai.com/oauth/token` as specified
above. On success, rewrite `auth.json` **atomically**, preserving all existing
top-level fields and `tokens` fields not returned by the refresh (notably
`id_token` when absent and `OPENAI_API_KEY`), updating `access_token`,
`refresh_token`, `id_token` (when returned), and `last_refresh` (now,
ISO-8601). This is the exact behavior Codex CLI itself and CodexBar use.
Never log token values.

**Snapshot mapping.**
- headline: `primary_window` → label "Session", reset line "5-hour window ·
  resets in …" (countdown style), `percent = used_percent`,
  `resetsAt = reset_at`.
- windows: headline + `secondary_window` → "Weekly" (date style). Missing
  windows are skipped; if `primary_window` is missing, the first available
  window becomes the headline; if none, treat as a decode error.
- plan pill: `plan_type` capitalized ("Pro", "Plus", "Business").
- Window labels derive from `limit_window_seconds` when present: the primary
  label is "Session · Nh" (rounded hours), the secondary "Weekly" when the
  duration is 7 days and "N-day" otherwise. When `limit_window_seconds` is
  absent the fixed labels "Session" / "Weekly" are used.

### Antigravity provider

`AntigravityProvider`, `id = .antigravity`, `displayName = "Antigravity"`.
Probe logic lives in a separate `AntigravityProbe` unit per platform so the
provider itself stays transport-agnostic and testable.

**Probe pipeline (each fetch).**
1. **Process scan** — macOS: `ps -ax -o pid=,command=`; Windows:
   `Win32_Process` query for name + command line. Match IDE language server
   (CSRF token required — tokenless IDE matches are skipped) or `agy` /
   `antigravity-cli` (empty CSRF accepted). Path-anchored matching so
   unrelated processes with "antigravity" in an argument don't match.
2. **Port discovery** — macOS: `lsof -nP -iTCP -sTCP:LISTEN -p <pid>`;
   Windows: `GetExtendedTcpTable` (P/Invoke) filtered to the PID's listening
   sockets.
3. **Connect probe** — `GetUnleashData` POST per port; first 200 wins. TLS
   trust override for the self-signed cert is **scoped to host 127.0.0.1
   only** and only on the probe's URLSession/HttpClient — never the shared
   transport. If all HTTPS ports fail and the IDE advertised
   `--extension_server_port`, retry that port over plain HTTP.
4. **Quota fetch** — `GetUserStatus`, falling back to
   `GetCommandModelConfigs`. Parse `clientModelConfigs[].quotaInfo`.

**Snapshot mapping.**
- One window per model config that has a `quotaInfo.remainingFraction`:
  label = the model's display label (e.g. "Claude", "Gemini Pro",
  "Gemini Flash"), `percent = (1 − remainingFraction) × 100` clamped to
  0–100, `resetsAt` parsed ISO-8601-or-epoch, countdown style.
- headline = highest-used window (Gemini provider convention).
- plan pill: `planName` when present, else hidden.

**Row semantics.** `detectCredentials()` (kept name; semantics here are
"should this provider get a row") returns true when a matching process is
running **or** a cached Antigravity snapshot newer than 7 days exists. So:
running → live row; Antigravity quit → row persists from `SnapshotCache` and
shows the existing `stale · Xm` treatment; cache older than 7 days → row
drops; never run + no cache → no row, no error. A probe failure while a cache
exists → `.stale`; with no cache → error row "Antigravity isn't running."

### Cross-cutting

- `ProviderID` gains `codex` and `antigravity` on both platforms; fix all
  exhaustive switches the compiler flags.
- Registration order (= panel order & menu-bar fallback priority): Claude,
  Gemini, Codex, Antigravity.
- Error enums gain `codexReauthRequired` and `antigravityNotRunning` cases
  (or the platform equivalent), with messages above wired into the existing
  per-row `messageView(for:)` / Windows error-string mapping.
- Windows widget board: `SnapshotFetcher` fallthrough becomes
  Claude → Gemini → Codex (reuses `CodexProvider`; one extra try/catch arm).
- No new preferences. Refresh interval, backoff, cache, and menu-bar provider
  selection all apply unchanged.

## Files touched

**Phase 1 — Codex**

- macOS: `Model/UsageModels.swift` (ProviderID), new `Data/CodexProvider.swift`
  + `Data/CodexCredentialsStore.swift` (auth.json load/refresh/write-back),
  `AppDelegate.swift` (register), error enum + panel message mapping,
  `Tests/TokenSpendieTests/CodexProviderTests.swift` + sanitized fixtures
  (`docs/superpowers/findings/fixtures/codex-*.json`).
- Windows: `Models/ProviderID.cs`, new `Data/CodexProvider.cs` +
  `Data/CodexCredentialsStore.cs`, `App.xaml.cs` (register),
  `TokenSpendie.WidgetProvider/Data/SnapshotFetcher.cs` (fallthrough),
  error mapping, `windows/tests/TokenSpendie.Windows.Tests/CodexProviderTests.cs`
  + fixtures.

**Phase 2 — Antigravity**

- macOS: `Model/UsageModels.swift` (ProviderID), new
  `Data/AntigravityProvider.swift` + `Data/AntigravityProbe.swift`
  (process/port/RPC) + decoder, `AppDelegate.swift`, error case,
  `Tests/TokenSpendieTests/AntigravityProviderTests.swift` + fixtures.
- Windows: `Models/ProviderID.cs`, new `Data/AntigravityProvider.cs` +
  `Data/AntigravityProbe.cs` (WMI command lines + `GetExtendedTcpTable`
  interop + localhost-scoped TLS override), `App.xaml.cs`, error mapping,
  tests + fixtures.

## Testing

**Unit (both platforms, mirrored):**
- Codex decoder: full payload; missing `secondary_window`; missing
  `primary_window` (fallback headline); missing both (decode error);
  plan-pill mapping.
- Refresh policy: stale `last_refresh` triggers refresh before fetch; 401 →
  one refresh + retry; second 401 → reauth error; refresh response without
  `refresh_token` keeps the old one.
- auth.json write-back: unknown fields and `OPENAI_API_KEY` preserved; output
  re-readable by the loader; atomic (temp + rename / `File.Replace`).
- Codex detection: `tokens` present → detected; API-key-only → not detected;
  `$CODEX_HOME` override.
- Antigravity process matcher: IDE line with CSRF → match; IDE line without
  CSRF → skipped; `agy` line without CSRF → match; lookalike argument →
  no match.
- Antigravity decoder: multi-model `GetUserStatus` fixture; configs missing
  `quotaInfo` skipped; ISO-8601 and epoch `resetTime`; headline = highest
  used; `GetCommandModelConfigs` fallback shape.
- Row semantics: process gone + fresh cache → detected/stale; cache > 7 days
  → not detected.

**Manual:**
- macOS: `codex login` then verify live Codex row, ring switch, 401 path by
  corrupting the access token; `agy` running → live Antigravity row; quit
  `agy` → stale row with countdown intact.
- Windows: same pass on a Windows machine/VM; verify widget-board card falls
  through to Codex when Claude and Gemini creds are absent.

## Phasing

1. **Phase 1 — Codex on both platforms.** Branch `feature/codex-provider`,
   PR into `develop`.
2. **Phase 2 — Antigravity on both platforms.** Branch
   `feature/antigravity-provider`, PR into `develop`.

The TEMP-debug modifications currently in the working tree (credential-cache
debug hooks) are unrelated and must not ride along on either feature branch.

## Sources

- [CodexBar](https://github.com/steipete/codexbar) — MIT; `docs/codex-oauth.md`,
  `docs/codex.md`, `docs/antigravity.md` + provider sources.
- [Codex with a ChatGPT plan](https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan)
- [Antigravity plans & limits](https://antigravity.google/docs/plans)
