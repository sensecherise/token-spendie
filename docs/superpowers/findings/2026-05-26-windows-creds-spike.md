# Windows Credential Storage Spike — Findings

**Date:** 2026-05-26
**Spec:** docs/superpowers/specs/2026-05-26-windows-port-design.md
**Goal:** Resolve U1 (Claude Code Windows credential location), U2 (JSON shape),
U3 (Gemini CLI Windows paths).

## Environment

- Windows version: Windows 11 Pro 10.0.26200
- Claude Code version: 2.1.150
- Gemini CLI version: <fill from `gemini --version`>
- Logged-in account email (redact domain if needed): <fill>

## U1 — Claude Code credential storage location

### Candidate 1 — `%USERPROFILE%\.claude\`

**Exists:** yes (`C:\Users\Cherise\.claude\`).

Files present (relevant):

| Name | Length | LastWriteTime |
|---|---|---|
| `.credentials.json` | 470 | 2026-05-27 00:13:05 |
| `settings.json` | 1224 | 2026-05-27 00:49:14 |
| `history.jsonl` | 4566 | 2026-05-27 01:04:44 |
| `mcp-needs-auth-cache.json` | 94 | 2026-05-27 00:17:04 |
| (subdirs: `backups`, `cache`, `downloads`, `hooks`, `ide`, `paste-cache`, `plans`, `plugins`, `projects`, `session-env`, `sessions`, `shell-snapshots`, `tasks`) | — | — |

`.credentials.json` present — strong candidate for OAuth storage.

### Candidate 2 — Windows Credential Manager

```
cmdkey /list | Select-String -Pattern "claude|anthropic" -CaseSensitive:$false
```

Result: **no matches**. Claude Code does not register a generic credential with Credential Manager on Windows.

### Candidate 3 — `%LOCALAPPDATA%\AnthropicClaude\`

Result: **directory does not exist**.

### Procmon trace

Skipped — Candidate 1 produced a clear hit (`.credentials.json` plain file).

**Resolution:** Credentials stored at `%USERPROFILE%\.claude\.credentials.json` as a plain-json file. Same path layout as macOS (`~/.claude/.credentials.json`), no DPAPI wrapping, no Credential Manager entry.

## U2 — Claude Code credential JSON shape

### Raw shape (secrets redacted)

```json
{
  "claudeAiOauth": {
    "accessToken": "<redacted>",
    "refreshToken": "<redacted>",
    "expiresAt": 1779844385628,
    "scopes": [
      "user:file_upload",
      "user:inference",
      "user:mcp_servers",
      "user:profile",
      "user:sessions:claude_code"
    ],
    "subscriptionType": "<redacted>",
    "rateLimitTier": "<redacted>"
  }
}
```

### Per-field probe

| Field | Type | Length / value |
|---|---|---|
| `accessToken` | string | 108 chars |
| `refreshToken` | string | 108 chars |
| `expiresAt` | Int64 | 13 digits — **milliseconds** since epoch (1779844385628 → 2026-05-30 UTC) |
| `scopes` | string[] | 5 entries, shown above |
| `subscriptionType` | string | 3 chars (account-tier indicator) |
| `rateLimitTier` | string | 21 chars (rate-limit-tier indicator) |

### Comparison with macOS

- **Top-level key:** `claudeAiOauth` — matches mac.
- **Core OAuth fields** (`accessToken`, `refreshToken`, `expiresAt`): present, same names, same types.
- **`expiresAt` units:** milliseconds (13 digits). Mac parser already has a heuristic for sec-vs-ms (per spec); same heuristic works.
- **Extra fields beyond mac shape:** `scopes`, `subscriptionType`, `rateLimitTier`. None are required to validate or refresh OAuth — they are advisory. M1 parser should tolerate unknown fields (it would anyway with a typed `OAuthCredentials` record + `JsonIgnoreCondition.WhenWritingNull` defaults).

### Sanitized fixture

Saved at `docs/superpowers/findings/fixtures/claude-credentials-sanitized.json`. Will seed `OAuthCredentialsParserTests` in M1.

**Resolution:** JSON shape matches mac (`claudeAiOauth.{accessToken,refreshToken,expiresAt}`) with three additional non-required fields (`scopes`, `subscriptionType`, `rateLimitTier`) that the parser should ignore. `expiresAt` units are **milliseconds**.

## U3 — Gemini CLI paths

<filled by Tasks 5 and 6>

## Resolution summary

<filled by Task 8>
