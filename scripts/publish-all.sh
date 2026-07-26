#!/usr/bin/env bash
# Publishes self-contained single-file Duet binaries for all three target RIDs
# into dist/<rid>/, then zips each.
set -euo pipefail
cd "$(dirname "$0")/.."

RIDS=(osx-arm64 win-x64 linux-x64)
for rid in "${RIDS[@]}"; do
  echo "== publish $rid =="
  dotnet publish src/Duet -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none \
    -o "dist/$rid"
  (cd dist && zip -qr "duet-$rid.zip" "$rid")
  echo "   -> dist/duet-$rid.zip"
done

echo "Done. Binaries in dist/{osx-arm64,win-x64,linux-x64}, zips in dist/."
