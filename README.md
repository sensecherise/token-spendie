# Token Spendie

A macOS menu bar widget that shows your Claude Code usage — the 5-hour session
window and weekly caps — in real time.

## Build

Requires the Swift toolchain (Xcode Command Line Tools). No Xcode needed.

    ./build.sh

This produces `build/TokenSpendie.app`.

## Install

### Homebrew (recommended)

If Homebrew is not installed, get it from [brew.sh](https://brew.sh):

    /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

Install Token Spendie:

    brew install --cask sensecherise/tap/token-spendie

### Direct download

1. Download `TokenSpendie-<version>.zip` from the [latest release](https://github.com/sensecherise/token-spendie/releases/latest).
2. Unzip and drag `TokenSpendie.app` to `/Applications`.
3. On first launch macOS may block it — open **System Settings → Privacy & Security** and click **Open Anyway**. This happens once.
4. When macOS asks for Keychain access, choose **Allow**.

> **After an update:** macOS may ask for Keychain access again on first launch. Choose **Allow** once more.

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
