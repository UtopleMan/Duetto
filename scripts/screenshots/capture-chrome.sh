#!/usr/bin/env bash
# Renders README screenshots using the app's own per-OS window chrome, headlessly.
# Produces docs/screenshots/{windows,macos,linux}.png from --chrome win|mac|gnome.
#
# Reproducible: builds the app, generates a curated sample home tree, points the app
# at it via $HOME so both panes and the Places rail show clean content, and captures
# one offscreen frame per chrome. These are the interim README images; real per-OS
# captures (Phase 6) can replace them.
set -euo pipefail
cd "$(dirname "$0")/../.."

OUT=docs/screenshots
mkdir -p "$OUT"

echo "== building app =="
dotnet build src/Duetto/Duetto.csproj -c Release >/tmp/ss-build.log 2>&1 || { tail -20 /tmp/ss-build.log; exit 1; }
BIN=$(find src/Duetto/bin/Release -name Duetto -type f -maxdepth 2 | head -1)
[ -x "$BIN" ] || { echo "app host not found under src/Duetto/bin/Release" >&2; exit 1; }

# --- curated sample home tree -------------------------------------------------
# Fixed, tidy path so the address bars read cleanly in the screenshots.
SAMPLE=/tmp/duetto-demo
rm -rf "$SAMPLE"; mkdir -p "$SAMPLE"
trap 'rm -rf "$SAMPLE"' EXIT
mk() { mkdir -p "$SAMPLE/$1"; }
file() { mkdir -p "$SAMPLE/$(dirname "$1")"; head -c "${2:-64}" /dev/zero | tr '\0' 'x' > "$SAMPLE/$1"; touch -t "${3:-202607151230}" "$SAMPLE/$1"; }

mk Documents; mk Downloads; mk Pictures; mk Music
mk Projects/duetto; mk Projects/website; mk Projects/notes
file "Documents/Invoice-2026.pdf" 240000 202606031015
file "Documents/Roadmap.md" 4200 202607101408
file "Downloads/duetto-1.0.0-osx-arm64.zip" 950000 202607280902
file "Downloads/photo.jpg" 380000 202607221830
file "Pictures/sunset.png" 720000 202607112015
file "Projects/duetto/README.md" 3800 202607301120
file "Projects/duetto/LICENSE" 1100 202607301120
file "Projects/website/index.html" 5600 202607190940
file "Projects/notes/todo.txt" 900 202607311000
file "todo.md" 1200 202607311215
file "notes.txt" 640 202607290815

render() { # chrome  out  leftsubdir
  echo "== $2.png ($1) =="
  # USER=you keeps a real login name out of the demo shell prompt.
  HOME="$SAMPLE" USER=you "$BIN" --chrome "$1" "$SAMPLE/$3" --screenshot "$OUT/$2.png" >/dev/null 2>&1 || true
  [ -s "$OUT/$2.png" ] || { echo "failed to render $2.png" >&2; exit 1; }
}

render win   windows Projects
render mac   macos   Projects
render gnome linux   Projects

echo "Done -> $OUT/{windows,macos,linux}.png"
