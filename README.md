# Token Spendie

A macOS menu bar widget that shows your Claude Code usage — the 5-hour session
window and weekly caps — in real time.

## Build

Requires the Swift toolchain (Xcode Command Line Tools). No Xcode needed.

    ./build.sh

This produces `build/TokenSpendie.app` and `build/TokenSpendie.zip`.

## Install

1. Unzip `TokenSpendie.zip` and move `TokenSpendie.app` to `/Applications`.
2. **First launch:** right-click the app → **Open**, then confirm. This is
   required once because the app is not notarized.
3. When macOS asks for Keychain access, choose **Allow** — the widget reads your
   Claude Code login token to fetch usage.

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

Send them `TokenSpendie.zip`. They follow the same Install steps. Each machine
uses its own Claude Code login automatically — there is nothing to configure.
