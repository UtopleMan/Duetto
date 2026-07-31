#!/usr/bin/env bash
# Builds dist/Duetto.app from a fresh self-contained publish for the given macOS RID.
# Usage: [VERSION=x.y.z] make-app-bundle.sh [osx-arm64|osx-x64]   (defaults: osx-arm64, 1.0.0)
# Requires macOS (sips + iconutil for the icon). Always republishes current source.
set -euo pipefail
cd "$(dirname "$0")/.."

RID="${1:-osx-arm64}"
VERSION="${VERSION:-1.0.0}"
APP=dist/Duetto.app
PUBLISH="dist/$RID"

echo "== publishing $RID (v$VERSION) =="
dotnet publish src/Duetto -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -p:Version="$VERSION" -o "$PUBLISH"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Icon: base PNG -> iconset -> icns
ICONSET=dist/duetto.iconset
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
python3 scripts/make-icon.py dist/icon-1024.png
for s in 16 32 64 128 256 512; do
  sips -z $s $s dist/icon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d dist/icon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Duetto.icns"
rm -rf "$ICONSET"

cp "$PUBLISH/Duetto" "$APP/Contents/MacOS/Duetto"
chmod +x "$APP/Contents/MacOS/Duetto"
# Avalonia native lib ships beside the binary even with single-file publish.
find "$PUBLISH" -maxdepth 1 -name "*.dylib" -exec cp {} "$APP/Contents/MacOS/" \;

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Duetto</string>
  <key>CFBundleDisplayName</key><string>Duetto</string>
  <key>CFBundleIdentifier</key><string>dk.truecon.duetto</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>Duetto</string>
  <key>CFBundleIconFile</key><string>Duetto.icns</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$APP" 2>/dev/null || true
echo "Built $APP ($RID v$VERSION)"
