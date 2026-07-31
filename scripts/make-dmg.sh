#!/usr/bin/env bash
# Builds dist/Duetto-<version>-<arch>.dmg — a clickable macOS install image containing
# Duetto.app with a drag-to-Applications shortcut. Rebuilds the .app from current source.
# Usage: [VERSION=x.y.z] make-dmg.sh [osx-arm64|osx-x64]   (defaults: osx-arm64, 1.0.0)
# Requires macOS (hdiutil, plus the tools make-app-bundle.sh needs).
set -euo pipefail
cd "$(dirname "$0")/.."

RID="${1:-osx-arm64}"
VERSION="${VERSION:-1.0.0}"
case "$RID" in
  osx-arm64) ARCH=arm64 ;;
  osx-x64)   ARCH=x64 ;;
  *) echo "Unsupported RID for dmg: $RID (expected osx-arm64|osx-x64)" >&2; exit 1 ;;
esac

VERSION="$VERSION" bash scripts/make-app-bundle.sh "$RID"

APP=dist/Duetto.app
STAGE=dist/dmg-staging
DMG="dist/Duetto-$VERSION-$ARCH.dmg"

rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"   # drag target for install

# UDZO = compressed, read-only — the standard distributable image.
hdiutil create -volname "Duetto" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"

echo "Built $DMG"
