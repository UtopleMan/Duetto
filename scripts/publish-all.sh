#!/usr/bin/env bash
# Publishes self-contained single-file Duetto binaries for all target RIDs into
# dist/<rid>/, then zips each as duetto-<version>-<rid>.zip.
# Usage: [VERSION=x.y.z] publish-all.sh   (default version 1.0.0)
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${VERSION:-1.0.0}"
RIDS=(osx-arm64 osx-x64 win-x64 linux-x64)
for rid in "${RIDS[@]}"; do
  echo "== publish $rid (v$VERSION) =="
  dotnet publish src/Duetto -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none \
    -p:Version="$VERSION" \
    -o "dist/$rid"
  (cd dist && zip -qr "duetto-$VERSION-$rid.zip" "$rid")
  echo "   -> dist/duetto-$VERSION-$rid.zip"
done

echo "Done. Zips: dist/duetto-$VERSION-*.zip"
