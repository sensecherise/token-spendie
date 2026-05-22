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
