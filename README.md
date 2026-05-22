# Token Spendie

A macOS menu bar widget that shows your Claude Code usage — the 5-hour session
window and weekly caps — in real time.

## Build

Requires the Swift toolchain (Xcode Command Line Tools). No Xcode needed.

    ./build.sh

This produces `build/TokenSpendie.app` and `build/TokenSpendie.zip`.

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

## Requirements

- macOS 13 (Ventura) or later.
- Claude Code installed and logged in (`claude` working in a terminal). The
  widget reads the token Claude Code already stores; it never logs you in.

## Using it

- The menu bar shows your session usage as a ring. Click it for the full
  breakdown (session, weekly, per-model weekly).
- In Settings you can enable a floating always-on-top panel, change the refresh
  interval, and toggle launch-at-login.

## Sharing with friends

Point them at the Homebrew commands above, or send them the `.pkg` from the
releases page. Each machine uses its own Claude Code login automatically — there
is nothing to configure.
