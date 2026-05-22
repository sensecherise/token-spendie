# Rename: Claude Usage Widget → Token Spendie

**Date:** 2026-05-22
**Branch:** `rename-token-spendie` (off `main`)

## Goal

Rename the project everywhere from "Claude Usage Widget" / `ClaudeUsage` to
"Token Spendie" / `TokenSpendie`. No behavior change — identifiers and
user-facing strings only.

## Name forms

| Context | Old | New |
|---|---|---|
| Display name (UI, README, plist DisplayName) | Claude Usage Widget | Token Spendie |
| Code identifier (target, dir, bundle, executable) | ClaudeUsageWidget / ClaudeUsage | TokenSpendie |
| Bundle identifier | com.cherise.ClaudeUsage | com.cherise.TokenSpendie |
| App bundle / zip | ClaudeUsage.app / .zip | TokenSpendie.app / .zip |

## Approach

`git mv` for directory renames (preserves history/blame), then targeted edits.
No global sed — `ClaudeUsage` can match unintended substrings.

## Changes

### 1. Swift package & source layout

- `Sources/ClaudeUsageWidget/` → `Sources/TokenSpendie/`
- `Tests/ClaudeUsageWidgetTests/` → `Tests/TokenSpendieTests/`
- `Package.swift`: package `name`, executable target name, test target name,
  test dependency, both `path:` values → `TokenSpendie` / `TokenSpendieTests`
- Every test file: `@testable import ClaudeUsageWidget` → `@testable import TokenSpendie`

### 2. App bundle

- `build.sh`: `APP=build/TokenSpendie.app`, `BIN_NAME=TokenSpendie`,
  zip → `TokenSpendie.zip`, header comment
- `Resources/Info.plist`:
  - `CFBundleName` → `TokenSpendie`
  - `CFBundleDisplayName` → `Token Spendie`
  - `CFBundleIdentifier` → `com.cherise.TokenSpendie`
  - `CFBundleExecutable` → `TokenSpendie`

### 3. User-facing UI strings

- `AppDelegate.swift:99` window title → `Token Spendie`
- `UI/PreferencesView.swift:39` header text → `Token Spendie`
- `UI/DetailPanelView.swift:179` panel header `CLAUDE USAGE` → `TOKEN SPENDIE`

### 4. Internal persistent IDs (renamed — accepted data loss)

- `Data/ManualTokenStore.swift:9` Keychain service →
  `com.cherise.TokenSpendie.token`. Manual token must be re-entered once.
- `Store/SnapshotCache.swift:7,10` Application Support dir → `TokenSpendie/`
  (doc comment + path). Old cache folder orphaned, harmless — app refetches.
- `Data/EndpointUsageProvider.swift:20` User-Agent → `TokenSpendie/1.0`
- `Store/UsageStore.swift:236` dispatch queue label → `TokenSpendie.network`
- `Tools/probe.swift:40` User-Agent → `TokenSpendie/probe`
- `Tools/token-probe.swift:15` User-Agent → `TokenSpendie/token-probe`

### 5. Docs & misc

- `README.md`: H1 → `# Token Spendie`; `.app` / `.zip` references renamed.
  Generic noun "widget" stays where it describes the kind of app.
- Rename files + update content:
  - `docs/superpowers/plans/2026-05-21-claude-usage-widget.md`
    → `2026-05-21-token-spendie.md`
  - `docs/superpowers/specs/2026-05-21-claude-usage-widget-design.md`
    → `2026-05-21-token-spendie-design.md`
- Content-only name updates (filenames unchanged):
  - `docs/superpowers/plans/2026-05-21-widget-theming-and-credentials.md`
  - `docs/superpowers/specs/2026-05-21-widget-theming-and-credentials-design.md`
  - `docs/superpowers/plans/2026-05-22-dropdown-ui-polish.md`
  - `docs/superpowers/specs/2026-05-22-dropdown-ui-polish-design.md`
- `.vscode/launch.json`, `.claude/settings.local.json`: update any old-name
  path references.

## Out of scope

- `build/ClaudeUsage.app`, `build/ClaudeUsage.zip`, `.build/` — stale build
  artifacts, regenerated correctly by `./build.sh`. Old copies may be deleted.
- `/Applications/ClaudeUsage.app` — installed copy outside the repo. User
  manually deletes it and installs the new `TokenSpendie.app`.

## Verification

1. `swift build` — compiles.
2. `swift test` — all tests pass with renamed test target.
3. `./build.sh` — produces `build/TokenSpendie.app` and `build/TokenSpendie.zip`.
4. `grep -ri 'claudeusage\|claude-usage' .` (excluding `.git`, `.build`, `build`)
   — returns nothing in tracked files.
