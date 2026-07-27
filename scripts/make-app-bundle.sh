#!/usr/bin/env bash
# Builds dist/Duetto.app from a fresh osx-arm64 self-contained publish.
# Requires macOS (sips + iconutil for the icon). Always republishes current
# source before bundling.
set -euo pipefail
cd "$(dirname "$0")/.."

APP=dist/Duetto.app
PUBLISH=dist/osx-arm64

# Always publish so the bundle ships current source. dotnet publish is
# incremental, so this is cheap when nothing changed; guarding on the binary's
# existence silently shipped stale code.
echo "== publishing osx-arm64 =="
dotnet publish src/Duet -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o "$PUBLISH"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Icon: base PNG -> iconset -> icns
ICONSET=dist/duet.iconset
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
python3 scripts/make-icon.py dist/icon-1024.png
for s in 16 32 64 128 256 512; do
  sips -z $s $s dist/icon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d dist/icon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Duet.icns"
rm -rf "$ICONSET"

cp "$PUBLISH/Duet" "$APP/Contents/MacOS/Duet"
chmod +x "$APP/Contents/MacOS/Duet"
# Avalonia native lib ships beside the binary even with single-file publish.
find "$PUBLISH" -maxdepth 1 -name "*.dylib" -exec cp {} "$APP/Contents/MacOS/" \;

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Duetto</string>
  <key>CFBundleDisplayName</key><string>Duetto</string>
  <key>CFBundleIdentifier</key><string>dk.truecon.duet</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>Duet</string>
  <key>CFBundleIconFile</key><string>Duet.icns</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$APP" 2>/dev/null || true
echo "Built $APP"
