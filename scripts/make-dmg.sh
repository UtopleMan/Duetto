#!/usr/bin/env bash
# Builds dist/Duetto.dmg — a clickable macOS install image containing Duetto.app
# with a drag-to-Applications shortcut. Rebuilds the .app from current source first.
# Requires macOS (hdiutil, plus the tools make-app-bundle.sh needs).
set -euo pipefail
cd "$(dirname "$0")/.."

bash scripts/make-app-bundle.sh

APP=dist/Duetto.app
STAGE=dist/dmg-staging
DMG=dist/Duetto.dmg

rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"   # drag target for install

# UDZO = compressed, read-only — the standard distributable image.
hdiutil create -volname "Duetto" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"

echo "Built $DMG"
