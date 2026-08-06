#!/usr/bin/env bash
# Regenerates the Homebrew cask (Casks/duetto.rb) in the tap from the built dmgs and
# pushes it. Run after scripts/make-dmg.sh has produced both arch dmgs for the version.
# Usage: [TAP_DIR=path] update-cask.sh <version> [--dry-run]
#   --dry-run prints the rendered cask to stdout and pushes nothing.
#   TAP_DIR defaults to ../homebrew-duetto (cloned from UtopleMan/homebrew-duetto if absent).
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:?usage: update-cask.sh <version> [--dry-run]}"
MODE="${2:-}"

ARM_DMG="dist/Duetto-$VERSION-arm64.dmg"
X64_DMG="dist/Duetto-$VERSION-x64.dmg"
for dmg in "$ARM_DMG" "$X64_DMG"; do
  if [ ! -f "$dmg" ]; then
    echo "missing $dmg — run 'VERSION=$VERSION scripts/make-dmg.sh <rid>' for both arches first" >&2
    exit 1
  fi
done

SHA_ARM=$(shasum -a 256 "$ARM_DMG" | awk '{print $1}')
SHA_X64=$(shasum -a 256 "$X64_DMG" | awk '{print $1}')

# Unquoted heredoc: $VERSION and the $SHA_* values expand; Ruby's #{version}/#{arch}
# carry no $ and stay literal in the output.
render()
{
cat <<EOF
cask "duetto" do
  arch arm: "arm64", intel: "x64"

  version "$VERSION"
  sha256 arm:   "$SHA_ARM",
         intel: "$SHA_X64"

  url "https://github.com/UtopleMan/Duetto/releases/download/v#{version}/Duetto-#{version}-#{arch}.dmg",
      verified: "github.com/UtopleMan/Duetto/"
  name "Duetto"
  desc "Fast, keyboard-driven dual-pane file manager"
  homepage "https://github.com/UtopleMan/Duetto"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: :big_sur

  app "Duetto.app"

  zap trash: "~/Library/Application Support/Duetto"

  caveats <<~CAVEATS
    Duetto is not signed or notarized, so Gatekeeper blocks it on first launch.
    Right-click Duetto in /Applications and choose Open, or clear the quarantine flag:

      xattr -dr com.apple.quarantine "/Applications/Duetto.app"
  CAVEATS
end
EOF
}

if [ "$MODE" = "--dry-run" ]; then
  render
  exit 0
fi

TAP_DIR="${TAP_DIR:-../homebrew-duetto}"
if [ ! -d "$TAP_DIR/.git" ]; then
  gh repo clone UtopleMan/homebrew-duetto "$TAP_DIR"
fi

mkdir -p "$TAP_DIR/Casks"
render > "$TAP_DIR/Casks/duetto.rb"

git -C "$TAP_DIR" add Casks/duetto.rb
if git -C "$TAP_DIR" diff --cached --quiet; then
  echo "Cask already at $VERSION — nothing to push."
  exit 0
fi
git -C "$TAP_DIR" commit -q -m "duetto $VERSION"
git -C "$TAP_DIR" push -q
echo "Cask updated to $VERSION and pushed to UtopleMan/homebrew-duetto."
