# Token Spendie — `.pkg` Installer & Homebrew Distribution

**Date:** 2026-05-22
**Status:** Approved design

## Problem

Token Spendie currently ships as `TokenSpendie.zip` (a zipped `.app`). Installing
means: unzip, drag to `/Applications`, then bypass Gatekeeper because the app is
not notarized. There is no easy way for other people to discover and install it.

Two goals:

1. Provide a `.pkg` so installation is a guided, native experience.
2. Provide a public install channel (Homebrew).

## Constraint: no Apple Developer Program membership

The app is **unsigned and not notarized** (free Apple account only). No package
format removes the Gatekeeper warning entirely without notarization. The design
works *with* that constraint rather than pretending to solve it:

- A `.pkg` payload is extracted by the macOS `installer` daemon. The resulting
  installed `.app` carries **no `com.apple.quarantine` flag** — it launches
  clean. The only friction moves to one step: opening the `.pkg` itself.
- Homebrew **strips the quarantine flag** from a downloaded `.pkg` before running
  it. So `brew install` produces **zero Gatekeeper prompts**, even unsigned.

Result: brew users get a frictionless install; direct downloaders get a real
installer with a single, expected, install-time prompt.

## Approach

`.pkg` installer + a custom Homebrew tap, both produced by CI on git tag.

Rejected alternatives:

- **`.dmg` drag-to-Applications** — the app stays quarantined, so Gatekeeper
  prompts on every first launch, and `right-click → Open` no longer bypasses it
  on macOS 15+. Worse UX than `.pkg` for an unsigned app.
- **Homebrew cask pointing straight at the `.zip`** — fine for brew users, but
  direct downloaders still get a quarantined app and no installer. Half a
  solution.

## Components

### 1. Artifacts

| Before | After |
|---|---|
| `TokenSpendie.app` + `TokenSpendie.zip` | `TokenSpendie.app` + `TokenSpendie-<version>.pkg` |

The `.zip` is dropped — the `.pkg` replaces it for every audience.

The `.app` bundle is **ad-hoc signed** during the build:

```
codesign --force --sign - build/TokenSpendie.app
```

This needs no Apple account. It seals the bundle, ensures a valid signature for
Apple Silicon, and avoids "app is damaged" errors.

### 2. pkg build — `scripts/package.sh`

New script `scripts/package.sh <version>`. It runs `build.sh` to produce and
ad-hoc-sign `build/TokenSpendie.app`, stages it under `build/pkgroot/`, then:

```
pkgbuild \
  --root build/pkgroot \
  --identifier com.cherise.TokenSpendie \
  --version "$VERSION" \
  --install-location /Applications \
  "build/TokenSpendie-$VERSION.pkg"
```

A component pkg from `pkgbuild` is sufficient — no `productbuild` distribution
wrapper, no license/welcome screens (YAGNI). Installs `TokenSpendie.app` into
`/Applications`.

The pkg identifier `com.cherise.TokenSpendie` matches the existing app bundle
identifier. The bundle identifier is **not** changed — renaming it would break
the existing Keychain ACL and stored user preferences.

### 3. Homebrew tap — new repo `sensecherise/homebrew-tap`

A separate repository is required: `brew tap <user>/<name>` resolves to a repo
named `homebrew-<name>`. The tap cannot live inside the main repo.

`Casks/token-spendie.rb`:

```ruby
cask "token-spendie" do
  version "1.0.0"
  sha256 "<filled by CI>"

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

User-facing install:

```
brew tap sensecherise/tap
brew install --cask token-spendie
```

Homebrew downloads the pkg, strips quarantine, runs `installer` — no prompts.

The tap repo also gets a short `README.md` with the two install commands.

### 4. CI release workflow — `.github/workflows/release.yml`

In the **main repo**. Trigger: push of a tag matching `v*`. Runner: `macos-15`.

Steps:

1. Checkout the repo.
2. `scripts/package.sh ${TAG#v}` → produces `build/TokenSpendie-<v>.pkg`.
3. Verify the pkg: `pkgutil --expand` into a temp dir, assert the payload
   contains `TokenSpendie.app`.
4. `gh release create "$TAG" build/TokenSpendie-*.pkg --generate-notes`
   (uses the default `GITHUB_TOKEN`).
5. Compute `shasum -a 256` of the pkg.
6. Checkout `sensecherise/homebrew-tap` using a fine-grained PAT.
7. Update `Casks/token-spendie.rb` — `version` and `sha256` — commit and push.

**Cross-repo write token.** The default `GITHUB_TOKEN` cannot write to another
repository. A fine-grained Personal Access Token, scoped to `contents: write`
on `homebrew-tap` only, is stored as the secret `HOMEBREW_TAP_TOKEN` in the main
repo and used for steps 6–7.

### 5. Version — single source of truth

The git tag `vX.Y.Z` is the source of truth. CI strips the leading `v` and
stamps the version into:

- the pkg (`pkgbuild --version`),
- the cask (`version` field),
- the app's `Info.plist` — `CFBundleShortVersionString` and `CFBundleVersion` —
  at build time (the committed `Info.plist` keeps placeholder values; CI
  overwrites them on a copy used for the bundle).

Local builds with no tag fall back to version `0.0.0-dev`.

### 6. Documentation

Rewrite the README install section:

- **Remove** the "right-click → Open" instruction. That Gatekeeper bypass was
  removed in macOS 15; the instruction is stale and no longer works.
- **Primary** install path: the two `brew` commands. Clean, no warnings.
- **Secondary** path: download the `.pkg` from GitHub Releases, then
  `right-click the pkg → Open`, or System Settings → Privacy & Security →
  "Open Anyway". This prompt is on the `.pkg` file, once, at install time.
- Note the keychain re-prompt behavior (see Caveats).

## Known caveats / accepted risks

- **Keychain re-prompt after each update.** The ad-hoc code signature's `cdhash`
  changes on every build. macOS keys a Keychain "Always Allow" decision to the
  app's code signature, so that decision does not survive an update — after
  updating, the user re-approves keychain access once. This is the accepted cost
  of staying unsigned; only a notarized Developer ID signature fully fixes it.
  Documented in the README.
- **The tap repo must stay unprotected** (or the PAT must be able to bypass
  protection) so CI can push the cask commit. Creating tags on the main repo's
  `main` branch is not affected by that branch's protection rules.

## Out of scope (YAGNI)

- Sparkle or any in-app auto-update.
- Notarization and Developer ID signing.
- Submission to the official `homebrew/cask` tap (requires notability metrics
  and a signed app).
- `.dmg` packaging.
- `productbuild` distribution wrapper with license/welcome screens.

## Testing

- A successful CI build is the primary gate.
- CI pkg sanity check: `pkgutil --expand` and assert the payload contains
  `TokenSpendie.app`.
- One manual smoke test before announcing a release: install the `.pkg` on a
  real Mac, confirm the app launches, then run the `brew install` path and
  confirm it installs with no prompts.

## Repository changes summary

Main repo (`sensecherise/token-spendie`):

- `scripts/package.sh` — new; builds the `.pkg`.
- `build.sh` — add ad-hoc signing; drop the `.zip` step.
- `.github/workflows/release.yml` — new; tag-triggered release pipeline.
- `Resources/Info.plist` — version keys become CI-stamped placeholders.
- `README.md` — install section rewrite.

New repo (`sensecherise/homebrew-tap`):

- `Casks/token-spendie.rb` — the cask.
- `README.md` — install instructions.
