# `.pkg` Installer & Homebrew Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Token Spendie as a `.pkg` installer plus a Homebrew cask, both produced automatically by CI on a git tag.

**Architecture:** A git tag `vX.Y.Z` triggers a GitHub Actions workflow on a macOS runner. The workflow builds an ad-hoc-signed `.app`, wraps it in a component `.pkg`, publishes a GitHub Release, then updates a cask in a separate `homebrew-tap` repo. The app stays unsigned/un-notarized — the design works with that constraint (pkg payloads install quarantine-free; Homebrew strips quarantine).

**Tech Stack:** Bash, `pkgbuild`, `codesign` (ad-hoc), `/usr/libexec/PlistBuddy`, GitHub Actions, Homebrew Cask (Ruby DSL).

**Spec:** `docs/superpowers/specs/2026-05-22-pkg-and-homebrew-distribution-design.md`

---

## File Structure

Main repo (`sensecherise/token-spendie`):

- `build.sh` — *modified.* Adds version stamping and ad-hoc signing; drops the `.zip` step. Stays responsible for producing one thing: `build/TokenSpendie.app`.
- `scripts/package.sh` — *new.* Calls `build.sh`, then wraps the app in a `.pkg`. Sole responsibility: the pkg.
- `.github/workflows/release.yml` — *new.* Tag-triggered release pipeline.
- `README.md` — *modified.* Install section rewrite.

Separate repo (`sensecherise/homebrew-tap`, user creates it):

- `Casks/token-spendie.rb` — *new.* The cask.
- `README.md` — *new.* Install instructions.

`Resources/Info.plist` is **not** edited — its `CFBundleShortVersionString` / `CFBundleVersion` stay as committed defaults; `build.sh` overwrites them in the *bundle copy* at build time.

---

## Task 1: Add version stamping + ad-hoc signing to `build.sh`, drop the zip

**Files:**
- Modify: `build.sh`

- [ ] **Step 1: Rewrite `build.sh`**

Replace the entire contents of `build.sh` with:

```bash
#!/usr/bin/env bash
# Builds TokenSpendie.app (ad-hoc signed, version-stamped).
# Version comes from the VERSION env var; defaults to 0.0.0-dev.
set -euo pipefail
cd "$(dirname "$0")"

APP="build/TokenSpendie.app"
BIN_NAME="TokenSpendie"
VERSION="${VERSION:-0.0.0-dev}"

echo "==> Compiling (release)"
swift build -c release

echo "==> Generating icon"
swift Tools/makeicon.swift
ICONSET="build/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
for s in 16 32 64 128 256 512; do
  sips -z $s $s     Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}.png"   >/dev/null
  sips -z $((s*2)) $((s*2)) Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o build/AppIcon.icns

echo "==> Assembling app bundle"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp ".build/release/$BIN_NAME" "$APP/Contents/MacOS/$BIN_NAME"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp build/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

echo "==> Stamping version $VERSION"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP/Contents/Info.plist"

echo "==> Ad-hoc signing"
codesign --force --sign - "$APP"

echo "==> Done: $APP"
```

Changes from the original: adds the `VERSION` variable, adds the version-stamping
block, adds the ad-hoc signing block, removes the `==> Zipping for sharing` step.

- [ ] **Step 2: Run the build**

Run: `./build.sh`
Expected: ends with `==> Done: build/TokenSpendie.app`. No `TokenSpendie.zip` is produced.

- [ ] **Step 3: Verify the signature**

Run: `codesign -dv build/TokenSpendie.app 2>&1 | grep -E 'Signature|Identifier'`
Expected: output contains `Signature=adhoc` and `Identifier=com.cherise.TokenSpendie`.

- [ ] **Step 4: Verify the version was stamped**

Run: `/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" build/TokenSpendie.app/Contents/Info.plist`
Expected: `0.0.0-dev`

Then run with an explicit version:
Run: `VERSION=9.9.9 ./build.sh && /usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" build/TokenSpendie.app/Contents/Info.plist`
Expected: `9.9.9`

- [ ] **Step 5: Confirm the committed `Info.plist` is untouched**

Run: `git diff --stat Resources/Info.plist`
Expected: no output (the file on disk is unchanged — only the bundle copy was stamped).

- [ ] **Step 6: Commit**

```bash
git add build.sh
git commit -m "Stamp version and ad-hoc sign the app bundle; drop zip step"
```

---

## Task 2: Create `scripts/package.sh` (pkg builder)

**Files:**
- Create: `scripts/package.sh`

- [ ] **Step 1: Create `scripts/package.sh`**

```bash
#!/usr/bin/env bash
# Builds TokenSpendie-<version>.pkg — a component installer that drops
# TokenSpendie.app into /Applications.
# Usage: scripts/package.sh [version]   (version defaults to 0.0.0-dev)
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:-0.0.0-dev}"
export VERSION

echo "==> Building app (version $VERSION)"
./build.sh

PKGROOT="build/pkgroot"
PKG="build/TokenSpendie-$VERSION.pkg"

echo "==> Staging payload"
rm -rf "$PKGROOT"
mkdir -p "$PKGROOT"
cp -R build/TokenSpendie.app "$PKGROOT/TokenSpendie.app"

echo "==> Building pkg"
rm -f "$PKG"
pkgbuild \
  --root "$PKGROOT" \
  --identifier com.cherise.TokenSpendie \
  --version "$VERSION" \
  --install-location /Applications \
  "$PKG"

echo "==> Done: $PKG"
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x scripts/package.sh`

- [ ] **Step 3: Run it**

Run: `scripts/package.sh 1.2.3`
Expected: ends with `==> Done: build/TokenSpendie-1.2.3.pkg`.

- [ ] **Step 4: Verify the pkg payload contains the app**

Run: `pkgutil --payload-files build/TokenSpendie-1.2.3.pkg | grep -c 'TokenSpendie.app/Contents/MacOS/TokenSpendie'`
Expected: `1`

- [ ] **Step 5: Verify the pkg version**

Run: `pkgutil --pkg-info-plist build/TokenSpendie-1.2.3.pkg | grep -A1 pkg-version`
Expected: output contains `1.2.3`.

- [ ] **Step 6: Commit**

```bash
git add scripts/package.sh
git commit -m "Add scripts/package.sh to build the .pkg installer"
```

---

## Task 3: Create the GitHub Actions release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create `.github/workflows/release.yml`**

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  release:
    runs-on: macos-15
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Build pkg
        run: |
          VERSION="${GITHUB_REF_NAME#v}"
          scripts/package.sh "$VERSION"

      - name: Verify pkg payload
        run: |
          VERSION="${GITHUB_REF_NAME#v}"
          pkgutil --payload-files "build/TokenSpendie-$VERSION.pkg" \
            | grep -q 'TokenSpendie.app/Contents/MacOS/TokenSpendie'

      - name: Create GitHub Release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release create "$GITHUB_REF_NAME" \
            build/TokenSpendie-*.pkg \
            --generate-notes

      - name: Update Homebrew cask
        env:
          TAP_TOKEN: ${{ secrets.HOMEBREW_TAP_TOKEN }}
        run: |
          VERSION="${GITHUB_REF_NAME#v}"
          PKG="build/TokenSpendie-$VERSION.pkg"
          SHA=$(shasum -a 256 "$PKG" | awk '{print $1}')
          git clone "https://x-access-token:${TAP_TOKEN}@github.com/sensecherise/homebrew-tap.git" tap
          cd tap
          sed -i '' -E "s/^  version \".*\"/  version \"$VERSION\"/" Casks/token-spendie.rb
          sed -i '' -E "s/^  sha256 \".*\"/  sha256 \"$SHA\"/" Casks/token-spendie.rb
          git config user.name  "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git add Casks/token-spendie.rb
          git commit -m "token-spendie $VERSION"
          git push
```

Notes for the engineer:
- `GITHUB_REF_NAME` is the tag (e.g. `v1.2.3`); `${GITHUB_REF_NAME#v}` strips the `v`.
- `secrets.GITHUB_TOKEN` is automatic. `secrets.HOMEBREW_TAP_TOKEN` must be added
  by the repo owner (a fine-grained PAT with `contents: write` on the
  `homebrew-tap` repo). Until that secret and repo exist, the last step fails —
  that is expected and is covered by Task 6.
- `sed -i ''` is BSD/macOS syntax; correct because the job runs on `macos-15`.

- [ ] **Step 2: Validate the YAML syntax**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml')); print('valid')"`
Expected: `valid`

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add tag-triggered release workflow"
```

---

## Task 4: Create the Homebrew tap repo files

**Files (in the SEPARATE `sensecherise/homebrew-tap` repo, not this repo):**
- Create: `Casks/token-spendie.rb`
- Create: `README.md`

Prerequisite: the repo owner has created the public repo `sensecherise/homebrew-tap`
and cloned it locally. Perform the steps below inside that clone.

- [ ] **Step 1: Create `Casks/token-spendie.rb`**

```ruby
cask "token-spendie" do
  version "0.0.0"
  sha256 "0000000000000000000000000000000000000000000000000000000000000000"

  url "https://github.com/sensecherise/token-spendie/releases/download/v#{version}/TokenSpendie-#{version}.pkg"
  name "Token Spendie"
  desc "Menu bar widget for Claude Code usage"
  homepage "https://github.com/sensecherise/token-spendie"

  depends_on macos: ">= :ventura"

  pkg "TokenSpendie-#{version}.pkg"

  uninstall quit:    "com.cherise.TokenSpendie",
            pkgutil: "com.cherise.TokenSpendie"

  zap trash: "~/Library/Preferences/com.cherise.TokenSpendie.plist"
end
```

The `version "0.0.0"` and all-zero `sha256` are placeholders. The release
workflow (Task 3) rewrites both lines on every tagged release. The cask becomes
installable after the first release runs.

- [ ] **Step 2: Create `README.md`**

```markdown
# Homebrew tap for Token Spendie

A macOS menu bar widget that shows your Claude Code usage.

## Install

    brew tap sensecherise/tap
    brew install --cask token-spendie

## Update

    brew upgrade --cask token-spendie

## Uninstall

    brew uninstall --cask token-spendie

See the main project: https://github.com/sensecherise/token-spendie
```

- [ ] **Step 3: Verify the cask parses (optional, requires Homebrew)**

Run: `brew style Casks/token-spendie.rb`
Expected: no offenses. (Skip this step if Homebrew is not installed; the file is
plain Ruby and will be exercised end-to-end in Task 6.)

- [ ] **Step 4: Commit (in the `homebrew-tap` repo)**

```bash
git add Casks/token-spendie.rb README.md
git commit -m "Add token-spendie cask"
git push
```

---

## Task 5: Rewrite the README install section

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Replace the `## Install` and `## Sharing with friends` sections**

In `README.md`, replace the existing `## Install` section **and** the existing
`## Sharing with friends` section with the following single block. Leave the
`## Build`, `## Requirements`, and `## Using it` sections unchanged.

```markdown
## Install

### With Homebrew (recommended)

    brew tap sensecherise/tap
    brew install --cask token-spendie

Homebrew installs the app with no Gatekeeper prompts.

### Direct download

1. Download `TokenSpendie-<version>.pkg` from the
   [latest release](https://github.com/sensecherise/token-spendie/releases/latest).
2. The app is not notarized, so macOS will not open the installer on a
   double-click. Either **right-click the `.pkg` → Open**, or open
   **System Settings → Privacy & Security**, scroll down, and click
   **Open Anyway**. This happens once, on the installer itself.
3. The installer places `Token Spendie.app` in `/Applications`. The installed
   app launches normally — no further prompts.
4. On first launch, when macOS asks for Keychain access, choose **Allow** — the
   widget reads your Claude Code login token to fetch usage.

> **After an update:** because the app is not notarized, macOS may ask for
> Keychain access again the first time the new version runs. Choose **Allow**
> once more.

## Sharing with friends

Point them at the Homebrew commands above, or send them the `.pkg` from the
releases page. Each machine uses its own Claude Code login automatically — there
is nothing to configure.
```

- [ ] **Step 2: Verify the stale instruction is gone**

Run: `grep -c 'right-click the app' README.md`
Expected: `0` (the old "right-click the app → Open" wording is removed; the new
text says "right-click the `.pkg`").

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Rewrite README install section for pkg and Homebrew"
```

---

## Task 6: First release — end-to-end smoke test

This task is performed by the repo owner. It is the manual gate from the spec's
Testing section. No code; it exercises everything built above.

- [ ] **Step 1: Create the tap repo and PAT (owner action)**

- Create the public repo `sensecherise/homebrew-tap` and push Task 4's files to it.
- Create a fine-grained Personal Access Token scoped to `contents: write` on the
  `homebrew-tap` repo only.
- In the `token-spendie` repo: Settings → Secrets and variables → Actions → add
  a secret named `HOMEBREW_TAP_TOKEN` with that PAT.

- [ ] **Step 2: Merge this branch and tag a release**

After this plan's branch is merged to `main`:

```bash
git checkout main && git pull
git tag v0.1.0
git push origin v0.1.0
```

- [ ] **Step 3: Watch the workflow**

Run: `gh run watch`
Expected: the `Release` workflow succeeds — all four steps green.

- [ ] **Step 4: Verify the release and the cask update**

Run: `gh release view v0.1.0 --json assets --jq '.assets[].name'`
Expected: `TokenSpendie-0.1.0.pkg`

Then check the tap repo's `Casks/token-spendie.rb` on GitHub — `version` should
be `0.1.0` and `sha256` a real 64-hex string (not all zeros).

- [ ] **Step 5: Test both install paths on a Mac**

```bash
brew tap sensecherise/tap
brew install --cask token-spendie
```

Expected: installs with no Gatekeeper prompts; `Token Spendie.app` appears in
`/Applications` and launches.

Then test the direct path: download `TokenSpendie-0.1.0.pkg` from the release,
open it (right-click → Open the first time), confirm the installer runs and the
app launches.

---

## Self-Review

**Spec coverage:**
- Artifacts (drop zip, ad-hoc sign) → Task 1.
- pkg build / `scripts/package.sh` → Task 2.
- Homebrew tap repo + cask → Task 4.
- CI release workflow → Task 3.
- Version single source of truth (tag → pkg + cask + Info.plist) → Tasks 1, 2, 3.
- Documentation rewrite → Task 5.
- Testing (CI build gate, `pkgutil` payload check, manual smoke test) → Tasks 2, 3, 6.
- Caveats (keychain re-prompt note, tap repo unprotected) → README in Task 5; tap-repo setup in Task 6.

**Placeholders:** The `0.0.0` version and all-zero `sha256` in Task 4 are
intentional cask seed values, documented as CI-overwritten. No TODO/TBD steps.

**Type consistency:** Identifier `com.cherise.TokenSpendie` is used identically
across `build.sh` (bundle), `pkgbuild --identifier`, and the cask's `quit:` /
`pkgutil:`. Pkg filename pattern `TokenSpendie-<version>.pkg` is consistent
across `package.sh`, the workflow, the cask `url`/`pkg`, and the README.
