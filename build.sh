#!/usr/bin/env bash
# Builds ClaudeUsage.app and a shareable zip.
set -euo pipefail
cd "$(dirname "$0")"

APP="build/ClaudeUsage.app"
BIN_NAME="ClaudeUsageWidget"

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

echo "==> Zipping for sharing"
( cd build && rm -f ClaudeUsage.zip && ditto -c -k --keepParent ClaudeUsage.app ClaudeUsage.zip )

echo "==> Done: $APP  and  build/ClaudeUsage.zip"
