# Windows Port — M0 Spike Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the three credential-storage unknowns (U1, U2, U3) called out in the Windows port spec by running a guided discovery spike on a real Windows machine. Produce a findings document that locks the credential-reader implementation chosen in M1 and the Gemini reader paths chosen in M3.

**Architecture:** No production code. The spike is a sequence of inspection commands run on a Windows host with Claude Code and Gemini CLI installed. Each task records observations into a findings document. The last task updates the spec to mark the unknowns resolved and unblocks the M1 plan.

**Tech Stack:** PowerShell 7, Sysinternals Procmon (only if file-system inspection fails), git, the existing Claude Code and Gemini CLIs.

**Spec:** [`docs/superpowers/specs/2026-05-26-windows-port-design.md`](../specs/2026-05-26-windows-port-design.md) — see "Credential reading" (U1, U2) and "Risks and unknowns" (U3).

**Prerequisites:** Access to a Windows 11 22H2+ machine with admin rights to install Sysinternals Procmon if needed. Claude Code and Gemini CLI installed and freshly logged in.

**Branch:** create `windows-port-m0-spike` off `develop` before the first commit.

---

## File structure

| File | Purpose | Created in |
|---|---|---|
| `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` | Captures every observation from the spike. The source of truth for U1/U2/U3 resolution. | Task 1 |
| `docs/superpowers/findings/fixtures/claude-credentials-sanitized.json` | Sanitized copy of the real Claude Code credential JSON on Windows. Used as a parser fixture in M1. | Task 4 |
| `docs/superpowers/findings/fixtures/gemini-logs-sanitized.json` | Sanitized copy of the Gemini CLI `logs.json` on Windows. Used as a parser fixture in M3. | Task 7 |
| `docs/superpowers/specs/2026-05-26-windows-port-design.md` | Spec — updated to mark U1/U2/U3 resolved with concrete locations. | Task 8 |

---

### Task 1: Bootstrap branch and findings document

**Files:**
- Create: `docs/superpowers/findings/2026-05-26-windows-creds-spike.md`

- [ ] **Step 1: Create the spike branch off `develop`**

Run on your Mac:
```bash
cd /Users/cherise/Documents/workspace/playground-project/claude-widget
git fetch origin
git checkout -b windows-port-m0-spike origin/develop
```

Expected: `Switched to a new branch 'windows-port-m0-spike'`.

- [ ] **Step 2: Create the findings directory and seed the document**

Run on your Mac:
```bash
mkdir -p docs/superpowers/findings/fixtures
```

Write `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` with this initial content:

```markdown
# Windows Credential Storage Spike — Findings

**Date:** 2026-05-26
**Spec:** docs/superpowers/specs/2026-05-26-windows-port-design.md
**Goal:** Resolve U1 (Claude Code Windows credential location), U2 (JSON shape),
U3 (Gemini CLI Windows paths).

## Environment

- Windows version: <fill from `winver`>
- Claude Code version: <fill from `claude --version`>
- Gemini CLI version: <fill from `gemini --version`>
- Logged-in account email (redact domain if needed): <fill>

## U1 — Claude Code credential storage location

<filled by Task 2>

## U2 — Claude Code credential JSON shape

<filled by Task 3>

## U3 — Gemini CLI paths

<filled by Tasks 5 and 6>

## Resolution summary

<filled by Task 8>
```

- [ ] **Step 3: Commit the empty scaffold**

Run on your Mac:
```bash
git add -f docs/superpowers/findings/2026-05-26-windows-creds-spike.md
git commit -m "spike: scaffold Windows credential-storage findings doc"
```

Expected: one new file committed; git status clean.

---

### Task 2: Capture Claude Code credential location on Windows (U1)

**Files:**
- Modify: `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` (section "U1")

- [ ] **Step 1: Confirm Claude Code is logged in**

Run on the Windows host (PowerShell):
```powershell
claude --version
claude /status
```

Expected: version string + a status output showing the logged-in user.

If `claude /status` reports "not logged in", run `claude /login` and complete the flow before continuing.

- [ ] **Step 2: Check candidate location 1 — `%USERPROFILE%\.claude\`**

Run on the Windows host:
```powershell
$path = Join-Path $env:USERPROFILE ".claude"
Get-ChildItem -Force $path -ErrorAction SilentlyContinue | Format-Table Name, Length, LastWriteTime
```

Record in the findings doc (section "U1"):
- Whether the directory exists.
- Every file listed, including `.credentials.json` if present.

- [ ] **Step 3: Check candidate location 2 — Windows Credential Manager**

Run on the Windows host:
```powershell
cmdkey /list | Select-String -Pattern "claude|anthropic" -CaseSensitive:$false
```

Record in the findings doc:
- Whether any matching generic credential exists.
- The exact `Target` string (e.g., `Claude Code-credentials`) if present.

- [ ] **Step 4: Check candidate location 3 — `%LOCALAPPDATA%\AnthropicClaude\`**

Run on the Windows host:
```powershell
$path = Join-Path $env:LOCALAPPDATA "AnthropicClaude"
Get-ChildItem -Force $path -ErrorAction SilentlyContinue | Format-Table Name, Length, LastWriteTime
```

Record presence and file list in the findings doc.

- [ ] **Step 5: If steps 2-4 all returned nothing, capture a Procmon trace of a fresh login**

Install Procmon if needed (Sysinternals). Then:

1. Run `claude /logout` in PowerShell.
2. Start Procmon. Set filter `Process Name is claude.exe` (or `node.exe` if Claude Code runs under Node).
3. Run `claude /login` and complete the flow.
4. Stop the Procmon capture once login completes.
5. Filter for `WriteFile` operations. Record every path that received a write during login.
6. Copy the top three candidate paths into the findings doc.

Skip this step if any of steps 2-4 already located the credential storage.

- [ ] **Step 6: Mark U1 resolved in the findings doc**

In `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` under "U1", write a one-line resolution:

```markdown
**Resolution:** Credentials stored at `<exact-path>` as a <plain-json|dpapi-blob|credential-manager-entry>.
```

- [ ] **Step 7: Commit**

Run on your Mac (after syncing the updated doc back from the Win host):
```bash
git add docs/superpowers/findings/2026-05-26-windows-creds-spike.md
git commit -m "spike: resolve U1 — locate Claude Code credentials on Windows"
```

---

### Task 3: Confirm Claude Code credential JSON shape (U2)

**Files:**
- Modify: `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` (section "U2")

- [ ] **Step 1: Read the credential blob, depending on the U1 result**

If U1 resolved to a JSON file (e.g., `%USERPROFILE%\.claude\.credentials.json`), run on the Windows host:
```powershell
Get-Content -Raw "<exact-path>"
```

If U1 resolved to Windows Credential Manager, run:
```powershell
Add-Type -AssemblyName System.Security
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cred {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct CREDENTIAL {
    public uint Flags; public uint Type;
    public IntPtr TargetName; public IntPtr Comment;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
    public uint CredentialBlobSize; public IntPtr CredentialBlob;
    public uint Persist; public uint AttributeCount;
    public IntPtr Attributes; public IntPtr TargetAlias; public IntPtr UserName;
  }
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern bool CredRead(string target, uint type, uint flags, out IntPtr cred);
  [DllImport("advapi32.dll")]
  public static extern void CredFree(IntPtr cred);
}
"@
$ok = [Cred]::CredRead("<target-from-U1>", 1, 0, [ref]([IntPtr]::Zero))   # adjust as needed
```
Note: if this path is required, expand the PowerShell wrapper inline before running — the snippet above is a starting scaffold, not final. Capture the JSON bytes via `Marshal.PtrToStringUni` over `CredentialBlob`.

If U1 resolved to a DPAPI-encrypted file, run:
```powershell
$bytes = [System.IO.File]::ReadAllBytes("<exact-path>")
$plain = [System.Security.Cryptography.ProtectedData]::Unprotect(
  $bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
[System.Text.Encoding]::UTF8.GetString($plain)
```

- [ ] **Step 2: Record the raw JSON structure**

Copy the JSON output. In the findings doc under "U2", paste it with **every secret value redacted to `"<redacted>"`**:

```json
{
  "claudeAiOauth": {
    "accessToken": "<redacted>",
    "refreshToken": "<redacted>",
    "expiresAt": 1781234567890
  }
}
```

Record:
- Whether the top-level key is `claudeAiOauth` (matches mac) or something else.
- Whether `expiresAt` is in seconds or milliseconds (the mac parser has a heuristic for both).
- Any additional fields not present in the mac JSON.

- [ ] **Step 3: Save a sanitized fixture file**

Save the redacted JSON to `docs/superpowers/findings/fixtures/claude-credentials-sanitized.json` exactly as recorded above. This file becomes the seed fixture for `OAuthCredentialsParserTests` in M1.

Verify no real tokens remain:
```powershell
Get-Content -Raw docs/superpowers/findings/fixtures/claude-credentials-sanitized.json |
  Select-String -Pattern "ey[A-Za-z0-9_-]{20,}|sk-ant-|oat[A-Za-z0-9_-]{20,}"
```

Expected: no output (no matches). If matches appear, redact and re-save before committing.

- [ ] **Step 4: Mark U2 resolved in the findings doc**

Write:

```markdown
**Resolution:** JSON shape matches mac (`claudeAiOauth.{accessToken,refreshToken,expiresAt}`) /
JSON shape differs from mac (see structure above). `expiresAt` units are <seconds|milliseconds>.
```

Pick the option that matches reality.

- [ ] **Step 5: Commit**

Run on your Mac:
```bash
git add docs/superpowers/findings/2026-05-26-windows-creds-spike.md \
        docs/superpowers/findings/fixtures/claude-credentials-sanitized.json
git commit -m "spike: resolve U2 — capture Claude Code credential JSON shape"
```

---

### Task 4: Confirm Gemini CLI credentials path

**Files:**
- Modify: `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` (section "U3" — credentials subsection)

- [ ] **Step 1: Confirm Gemini CLI is logged in**

Run on the Windows host:
```powershell
gemini --version
gemini /status
```

Expected: version and a status line indicating logged-in state. If not logged in, run `gemini /login` and complete the flow.

- [ ] **Step 2: Check `%USERPROFILE%\.gemini\`**

Run on the Windows host:
```powershell
$path = Join-Path $env:USERPROFILE ".gemini"
Get-ChildItem -Force -Recurse $path -ErrorAction SilentlyContinue |
  Select-Object FullName, Length, LastWriteTime
```

Record every file found in the findings doc under "U3 — credentials".

- [ ] **Step 3: Check `%LOCALAPPDATA%\Google\Gemini\` and `%APPDATA%\Google\Gemini\`**

Run on the Windows host:
```powershell
$local   = Join-Path $env:LOCALAPPDATA "Google\Gemini"
$roaming = Join-Path $env:APPDATA      "Google\Gemini"
foreach ($p in @($local, $roaming)) {
  if (Test-Path $p) {
    "== $p =="
    Get-ChildItem -Force -Recurse $p | Select-Object FullName, Length, LastWriteTime
  }
}
```

Record findings.

- [ ] **Step 4: Mark Gemini credentials path resolved**

Under "U3 — credentials", write:

```markdown
**Resolution:** Gemini CLI credentials on Windows at `<exact-path>`.
File existence check (used by `detectCredentials()` on mac) maps to: `<exact-path>`.
```

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/findings/2026-05-26-windows-creds-spike.md
git commit -m "spike: resolve U3 (creds) — locate Gemini CLI credentials on Windows"
```

---

### Task 5: Confirm Gemini CLI `logs.json` path and shape

**Files:**
- Create: `docs/superpowers/findings/fixtures/gemini-logs-sanitized.json`
- Modify: `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` (section "U3" — logs subsection)

- [ ] **Step 1: Generate at least one Gemini request so `logs.json` is non-empty**

Run on the Windows host:
```powershell
gemini "hello from the spike"
```

Expected: model reply printed. This forces a log write.

- [ ] **Step 2: Locate `logs.json`**

Run on the Windows host:
```powershell
$candidates = @(
  (Join-Path $env:USERPROFILE ".gemini\logs.json"),
  (Join-Path $env:USERPROFILE ".gemini\tmp\logs.json"),
  (Join-Path $env:LOCALAPPDATA "Google\Gemini\logs.json")
)
foreach ($p in $candidates) {
  if (Test-Path $p) { "Found: $p"; (Get-Item $p).Length; (Get-Item $p).LastWriteTime }
}
```

If none match, run:
```powershell
Get-ChildItem -Force -Recurse -Path $env:USERPROFILE, $env:LOCALAPPDATA, $env:APPDATA `
  -Filter "logs.json" -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -match "gemini" } |
  Select-Object FullName, Length, LastWriteTime
```

Record the exact path in the findings doc under "U3 — logs".

- [ ] **Step 3: Capture the JSON shape**

Run on the Windows host:
```powershell
Get-Content -Raw "<exact-path>" | Out-String
```

Record the top-level structure (array vs object), the per-entry fields, and the timestamp field name and units. Compare to the shape `GeminiUsageReader.swift` expects.

- [ ] **Step 4: Save a sanitized fixture**

Manually redact any prompt or response text that contains real user content. Save the result to `docs/superpowers/findings/fixtures/gemini-logs-sanitized.json`. Aim for 2-3 entries — enough to validate parsing logic in M3.

Verify no real prompt text remains:
```powershell
Get-Content -Raw docs/superpowers/findings/fixtures/gemini-logs-sanitized.json |
  Select-String -Pattern "<unique-string-from-your-own-test-prompt>"
```

Expected: no output if redacted correctly.

- [ ] **Step 5: Mark U3 logs resolved**

Under "U3 — logs", write:

```markdown
**Resolution:** Gemini `logs.json` on Windows at `<exact-path>`.
Top-level shape: <array|object>. Timestamp field: `<field-name>` (<seconds|milliseconds>).
Per-entry fields match mac: <yes|no — list diffs>.
```

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/findings/2026-05-26-windows-creds-spike.md \
        docs/superpowers/findings/fixtures/gemini-logs-sanitized.json
git commit -m "spike: resolve U3 (logs) — capture Gemini logs.json path and shape"
```

---

### Task 6: Write the resolution summary

**Files:**
- Modify: `docs/superpowers/findings/2026-05-26-windows-creds-spike.md` (section "Resolution summary")

- [ ] **Step 1: Fill the summary section**

In `docs/superpowers/findings/2026-05-26-windows-creds-spike.md`, complete the "Resolution summary" section. Use this exact template:

```markdown
## Resolution summary

### U1 — Claude Code credential location
- Path / mechanism: <one of: `%USERPROFILE%\.claude\.credentials.json` | Windows Credential Manager target `<name>` | DPAPI file at `<path>`>
- Concrete reader class to build in M1: `<one of: ClaudeJsonFileReader | WindowsCredentialManagerReader | DpapiFileReader>`

### U2 — JSON shape
- Top-level key: `claudeAiOauth` (matches mac) | `<other-key>` (differs from mac)
- `expiresAt` units: seconds | milliseconds
- Extra fields beyond mac shape: <none | list>
- Parser changes needed in M1: <none | describe>

### U3 — Gemini CLI
- Credentials path: `<exact-path>`
- `logs.json` path: `<exact-path>`
- `logs.json` shape: <matches mac | differs — describe>
- Parser changes needed in M3: <none | describe>

### Impact on spec

- [ ] Section "Module mapping" placeholder `<concrete reader chosen after U1 spike>` → replace with `<class-name>`.
- [ ] Section "Credential reading" — narrow the three candidates down to the confirmed one. Add a footnote with the verified path.
- [ ] Section "Project layout" `<ConcreteCredentialReader>.cs` → replace with `<class-name>.cs`.
- [ ] If U2 surfaced any JSON shape divergence, add it to `OAuthCredentialsParser` design notes.
- [ ] If U3 surfaced any Gemini divergence, add it to the `GeminiUsageReader` design notes (preview for M3 plan).
```

- [ ] **Step 2: Commit the summary**

```bash
git add docs/superpowers/findings/2026-05-26-windows-creds-spike.md
git commit -m "spike: summarize findings and list spec edits required"
```

---

### Task 7: Apply spike findings to the spec

**Files:**
- Modify: `docs/superpowers/specs/2026-05-26-windows-port-design.md`

- [ ] **Step 1: Replace the credential-reader placeholder**

In the spec, find the line in the "Module mapping" table:

```markdown
| `Data/KeychainReader` | `Data/<concrete reader chosen after U1 spike>` behind `Data/ICredentialReader` |
```

Replace `<concrete reader chosen after U1 spike>` with the class name decided in Task 6 (e.g., `ClaudeJsonFileReader`).

- [ ] **Step 2: Replace the project-layout placeholder**

In the spec's "Project layout" code block, find:

```
          <ConcreteCredentialReader>.cs   # name + impl chosen after U1 spike
```

Replace with the chosen class file name (e.g., `ClaudeJsonFileReader.cs`) and remove the `# name + impl chosen after U1 spike` comment.

- [ ] **Step 3: Narrow the candidate list in "Credential reading"**

Find this paragraph in the spec:
```markdown
**Open unknown (must resolve in Task 0 of implementation plan):** where Claude Code stores OAuth credentials on Windows. Candidates, in expected likelihood order:
```

Replace the entire paragraph + numbered list with:
```markdown
**Storage location** (confirmed by M0 spike, [findings](../findings/2026-05-26-windows-creds-spike.md)): `<exact-path-or-mechanism>`.
```

Then delete the "Spike procedure (Task 0)" subsection (it's obsolete — the spike is done).

- [ ] **Step 4: If U2 surfaced JSON shape divergence, document the parser change**

If Task 3 found the JSON shape matches mac: no change to "Credential reading" section.
If it differs: under "Credential reading", add a subsection:
```markdown
**Parser divergence from mac:** <describe — e.g., top-level key is `oauth_creds` not `claudeAiOauth`; the parser needs a Windows-specific path.>
```

- [ ] **Step 5: Cross-reference the findings doc from the spec**

At the top of the spec, just under the `**Date:** 2026-05-26.` line, add:
```markdown
**M0 spike findings:** [`docs/superpowers/findings/2026-05-26-windows-creds-spike.md`](../findings/2026-05-26-windows-creds-spike.md)
```

- [ ] **Step 6: Mark U1, U2, U3 resolved in the Risks table**

In the "Risks and unknowns" → "Hard unknowns" table, change the three rows:

From:
```markdown
| U1 | Where Claude Code stores OAuth credentials on Windows | Spike in Task 0 (procedure above). |
```

To:
```markdown
| U1 | ~~Where Claude Code stores OAuth credentials on Windows~~ | **Resolved (M0):** `<exact-path-or-mechanism>`. |
```

Repeat the same strikethrough-and-resolve pattern for U2 and U3.

- [ ] **Step 7: Commit the spec update**

```bash
git add docs/superpowers/specs/2026-05-26-windows-port-design.md
git commit -m "spec: apply M0 spike findings (resolve U1, U2, U3)"
```

---

### Task 8: Verify the branch is ready and hand off to M1

**Files:** (none)

- [ ] **Step 1: Run the full self-check**

Run on your Mac:
```bash
git status
git log --oneline origin/develop..HEAD
```

Expected from `git status`: working tree clean.
Expected from `git log`: 7 commits — one per Task (1, 2, 3, 4, 5, 6, 7).

- [ ] **Step 2: Confirm no real secrets leaked into git**

Run on your Mac:
```bash
git log -p origin/develop..HEAD -- docs/superpowers/findings/ |
  grep -E 'ey[A-Za-z0-9_-]{20,}|sk-ant-|oat[A-Za-z0-9_-]{20,}' || echo "clean"
```

Expected: `clean`. If anything matches, the redaction failed — abort, fix the fixture files, amend the relevant commits via `git rebase -i`, and re-run the check before continuing.

- [ ] **Step 3: Push the branch and open a PR to `develop`**

```bash
git push -u origin windows-port-m0-spike
gh pr create --base develop --title "spike: Windows credential storage findings (M0)" \
  --body "$(cat <<'EOF'
## Summary
- Resolves U1, U2, U3 from the Windows port spec.
- Adds findings doc at `docs/superpowers/findings/2026-05-26-windows-creds-spike.md`.
- Adds sanitized JSON fixtures for M1 (Claude creds) and M3 (Gemini logs).
- Updates the spec to lock the credential-reader class name.

## Test plan
- [ ] Reviewer confirms findings doc lists every checked path explicitly (no hand-waves).
- [ ] Reviewer confirms fixture files contain no real tokens (`grep -E 'ey[A-Za-z0-9_-]{20,}|sk-ant-|oat[A-Za-z0-9_-]{20,}'` is silent).
- [ ] Reviewer confirms spec U1/U2/U3 rows show "Resolved (M0)".

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL printed.

- [ ] **Step 4: Notify the user that M1 planning is unblocked**

Reply in chat:

> "M0 spike complete. PR: <url>. Once merged to `develop`, run `/loop` or ask me to write the M1 plan — it'll use the resolved credential-reader class name and the fixtures from the findings."

---

## Self-review

**Spec coverage:** This plan covers M0 only. M0's goal from the spec is "Resolve U1 / U2 / U3 on a real Windows box". Tasks 2, 3, 4-5 resolve U1, U2, U3 respectively. Task 6 produces the summary the M1 plan will consume. Task 7 reflects findings into the spec. Task 8 ships the work. Full coverage.

**Placeholder scan:** Every "<…>" appearing in this plan is intentional — they are recording slots the engineer fills with observed values during execution. None are author placeholders.

**Type consistency:** No code types defined in this plan (it's discovery work). The one type name referenced (`ICredentialReader`) is defined in the spec, not here. The class name chosen in Task 6 propagates through Task 7's spec edits consistently.

**Branch and secret discipline:** Branch creation is Task 1 Step 1 (matches `feedback-branch-per-phase`). Secret leak check is Task 8 Step 2 (catches fixture-redaction misses before push).
