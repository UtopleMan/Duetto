#!/usr/bin/env bash
# Publishes self-contained single-file Duetto binaries for all three target RIDs
# into dist/<rid>/, then zips each.
set -euo pipefail
cd "$(dirname "$0")/.."

RIDS=(osx-arm64 win-x64 linux-x64)
for rid in "${RIDS[@]}"; do
  echo "== publish $rid =="
  dotnet publish src/Duetto -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none \
    -o "dist/$rid"
  (cd dist && zip -qr "duetto-$rid.zip" "$rid")
  echo "   -> dist/duetto-$rid.zip"
done

echo "Done. Binaries in dist/{osx-arm64,win-x64,linux-x64}, zips in dist/."
