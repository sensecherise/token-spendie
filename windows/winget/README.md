# WinGet manifest templates

This directory contains a snapshot of the WinGet manifest triplet we submit to
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) for
`sensecherise.TokenSpendie`. The CI release workflow regenerates the manifest
fresh on each stable tag via `wingetcreate`; the files in `template/` are
**documentation only** and are not consumed by CI (except on the very first
stable release, when the workflow seeds the new manifest from these templates).

## When to edit `template/`

- Adding or removing a tag.
- Changing the short description.
- Updating publisher URLs.
- Tweaking the long description.

After editing `template/`, the next stable release will pick up the changes
because `wingetcreate update` reads the *previous* manifest from
`microsoft/winget-pkgs` — meaning template edits don't propagate until a release
ships. To force an update without a release, edit
[`manifests/s/sensecherise/TokenSpendie/<version>/`](https://github.com/microsoft/winget-pkgs/tree/master/manifests/s/sensecherise/TokenSpendie)
directly via a hand-authored PR.

## What CI does

`.github/workflows/windows-release.yml` runs `submit-winget` after the main
`release` job succeeds, but only for stable tags (no `-rc`, `-beta`, etc.):

1. Downloads `wingetcreate.exe` (pinned version).
2. Constructs the installer URL from the just-published GitHub Release.
3. On first submission: stages from `template/`, patches version + URL + SHA256,
   runs `wingetcreate submit <dir>`. On later releases: `wingetcreate update`.
4. `wingetcreate` opens a PR against `microsoft/winget-pkgs` from the user's
   personal fork. Microsoft moderators review and merge.

Required GitHub repo Secret: `WINGET_PAT` — a Personal Access Token owned by
the repo owner with `public_repo` scope. See the M5 plan for setup steps.

## Manual submission fallback

If `wingetcreate` breaks or the schema changes:

1. Copy `template/*.yaml` into a fork of `microsoft/winget-pkgs` under
   `manifests/s/sensecherise/TokenSpendie/<version>/`.
2. Replace the placeholder values (`PackageVersion`, `InstallerUrl`,
   `InstallerSha256`, `ReleaseDate`) with the real values for the current
   release.
3. Validate locally with `winget validate --manifest <dir>`.
4. Open a PR against `microsoft/winget-pkgs`.

## Calibration after first signed release

The `InstallerType: nullsoft` value in `installer.yaml` is the planner's best
guess for Velopack's NSIS-style installer. After the first stable release lands,
run `wingetcreate new --urls <real-installer-url>` locally and inspect the
detected `InstallerType` and `Scope`. Update `template/installer.yaml` to match
if the auto-detection differs.
