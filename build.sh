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
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString '$VERSION'" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion '$VERSION'" "$APP/Contents/Info.plist"

echo "==> Ad-hoc signing"
codesign --force --sign - "$APP"

echo "==> Done: $APP"
