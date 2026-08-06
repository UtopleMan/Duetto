# Publish Duetto on Homebrew + README quick install

Ship Duetto as a Homebrew **Cask** via a custom tap (`UtopleMan/homebrew-duetto`,
installed as `brew install --cask utopleman/duetto`), automate the cask's
version/sha bump on each release, and add a `## Quick install` section to the top
of the README.

## Locked decisions
- **Distribution:** custom tap `UtopleMan/homebrew-duetto` (public). Install command:
  `brew install --cask utopleman/duetto`. NOT the official `homebrew/cask` (new,
  unsigned project won't pass notability/notarization scrutiny).
- **Artifact:** the arch-specific dmgs already attached to GitHub release `v1.4.0`
  (`Duetto-1.4.0-arm64.dmg`, `Duetto-1.4.0-x64.dmg`). Cask uses `arch`-templated url
  + per-arch sha256.
- **v1.4.0 hashes:** arm64 `847eb3d1b207d588140d4552f42c231ae61a0e9b3410b7bb267b4337083b65df`,
  x64 `ee32b201b57e38f7ced065f439e0b95436ceb214666fa88ad559ddde4359f597`.
- **Unsigned app:** cask ships a `caveats` block telling the user to clear quarantine
  (Gatekeeper blocks unsigned apps; nothing in the cask can bypass that).
- **Automation:** a local `scripts/update-cask.sh <version>` regenerates the cask from
  the built dmgs and pushes it to a tap checkout — run as part of the existing manual
  release flow (repo has no CI).
- **README:** dedicated `## Quick install` section after the badges, before the
  screenshots. The detailed `## Install` table stays as-is below it.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary**; run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Custom tap + cask
Status: Complete

- [x] Create public repo `UtopleMan/homebrew-duetto` (`gh repo create`).
- [x] Write `Casks/duetto.rb`: `arch arm:"arm64",intel:"x64"`; `version "1.4.0"`;
      per-arch `sha256`; `url ".../releases/download/v#{version}/Duetto-#{version}-#{arch}.dmg"`;
      `name`, `desc`, `homepage`; `depends_on macos: :big_sur` (symbol form — string
      `">= :big_sur"` is deprecated in Homebrew 6); `app "Duetto.app"`; `livecheck`
      on GitHub releases; `zap trash: "~/Library/Application Support/Duetto"`;
      `caveats` for unsigned/quarantine.
- [x] Add a short tap `README.md` (one-liner + install command).
- [x] Commit and push the tap.

### Verification Plan
- `brew tap UtopleMan/duetto && brew info --cask utopleman/duetto` → shows version `1.4.0`.
- `brew audit --cask --tap utopleman/duetto duetto` → no errors (style warnings acceptable).
- `brew install --cask --force utopleman/duetto` → installs `/Applications/Duetto.app`;
  `/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" /Applications/Duetto.app/Contents/Info.plist` → `1.4.0`.

### Phase Summary
Done. Tap: https://github.com/UtopleMan/homebrew-duetto (public), cask at
`Casks/duetto.rb`. **Verified end-to-end**: `brew audit --cask --tap utopleman/duetto
duetto` clean (no warnings/errors); removed the manually-installed app and ran
`brew install --cask utopleman/duetto/duetto` — Homebrew downloaded the v1.4.0 dmg
from the GitHub release, verified the sha256, and installed `/Applications/Duetto.app`
(v1.4.0, arm64). `brew list --cask` shows `duetto` "Installed (on request)". App
launches after clearing quarantine.

**Key facts for future agents:**
- Install command is `brew install --cask utopleman/duetto/duetto` (full
  `user/tap/cask`). The shorthand `utopleman/duetto` does **not** resolve — Homebrew
  reads a two-part ref as a tap, not a cask. After `brew tap utopleman/duetto`, the
  bare token `brew install --cask duetto` also works.
- `depends_on macos:` must use the **symbol** form (`:big_sur`); the `">= :big_sur"`
  string form is deprecated and made the cask fail to load in Homebrew 6.0.12.
- Custom taps are always loaded from the local clone (not the Homebrew API), so no
  API publish step is needed — a push to the tap repo is live immediately after
  `brew update` / `git -C <tap> pull`.

## Phase 2: Automate cask updates on release
Status: Complete

- [x] Add `scripts/update-cask.sh <version>` to the main repo: computes sha256 of
      `dist/Duetto-<version>-{arm64,x64}.dmg`, renders `Casks/duetto.rb` from a
      heredoc template, writes it into a tap checkout (`TAP_DIR` env, default
      `../homebrew-duetto`; clone via `gh` if missing), commits + pushes. Support
      `--dry-run` to print the rendered cask without writing.
- [x] Document the step in the release flow (CONTRIBUTING.md `## Releases` →
      `### Homebrew cask`) so it runs right after `make-dmg.sh`.

### Verification Plan
- `bash scripts/update-cask.sh 1.4.0 --dry-run` → prints ruby whose hashes match the
  Phase-1 dmg hashes.
- Rendered output piped to `ruby -c -` → `Syntax OK`.

### Phase Summary
Done. `scripts/update-cask.sh <version> [--dry-run]` renders the cask from the built
dmgs and pushes it to the tap. **Verified**: dry-run output passes `ruby -c` and
contains both Phase-1 sha256 hashes; the real run pushed the regenerated cask to
`UtopleMan/homebrew-duetto`, the Homebrew tap clone pulled it, and
`brew audit --cask --tap utopleman/duetto duetto` stayed clean. The generator output
is now byte-identical to the deployed cask.

**Notes for future agents:**
- The template uses an unquoted bash heredoc so `$VERSION`/`$SHA_*` expand while
  Ruby's `#{version}`/`#{arch}` (no `$`) stay literal — don't quote the heredoc.
- Build both dmgs first (`VERSION=<v> scripts/make-dmg.sh osx-arm64` and `osx-x64`);
  the script fails fast if `dist/Duetto-<v>-{arm64,x64}.dmg` are missing.
- Documented in `CONTRIBUTING.md` → `## Releases` → `### Homebrew cask`.

## Phase 3: README quick install
Status: Complete

- [x] Insert a `## Quick install` section after the badges block (before the
      screenshots): macOS `brew install --cask utopleman/duetto/duetto` fenced block,
      and a "Windows / Linux → Releases" line + unsigned/quarantine note. Keep the
      detailed `## Install` section below.

### Verification Plan
- `grep -n "brew install --cask utopleman/duetto" README.md` → matches once near the top
  (line < screenshots heading `### macOS`).
- `grep -nE '^## Quick install' README.md` → present.

### Phase Summary
Done. Added `## Quick install` at README line 20 (heading), brew one-liner at line 25
— both above the first screenshot heading `### macOS` at line 37 (ORDER-OK). The
detailed `## Install` table remains below and is cross-linked. Command uses the
correct full ref `brew install --cask utopleman/duetto/duetto` (not the shorthand).

## Phase 4: Commit + push main repo
Status: Complete

- [x] Commit `scripts/update-cask.sh` + README + CONTRIBUTING to `main`, pushed.
- [x] Add the brew one-liner to the `v1.4.0` GitHub release notes.

### Verification Plan
- `git push` clean; `git status` shows only the intended files committed.
- `gh release view v1.4.0 --json body -q .body | grep -q "brew install --cask utopleman/duetto"` → match.

### Phase Summary
Done. Two commits on `main`: `feat(scripts): add update-cask.sh…` (14207eb) and
`docs: Homebrew quick install + cask release step` (429f438), pushed to
`origin/main`. The pre-existing unrelated `CLAUDE.md` whitespace change was left
unstaged. The `v1.4.0` release notes now lead with the Homebrew install line
(grep count = 1). This plan file is committed separately.

## Final Recap
Duetto is now installable via Homebrew on macOS: `brew install --cask
utopleman/duetto/duetto`. A public tap repo `UtopleMan/homebrew-duetto` holds the
cask (`Casks/duetto.rb`), pinned to release v1.4.0's dmgs with per-arch sha256 and
GitHub `livecheck`. Installs are verified end-to-end (brew downloaded the dmg,
checked the hash, placed `/Applications/Duetto.app` v1.4.0 arm64).
`scripts/update-cask.sh` regenerates and pushes the cask from the built dmgs each
release, documented in `CONTRIBUTING.md`. The main README leads with a
`## Quick install` section featuring the brew command, and the v1.4.0 GitHub
release notes include it too.

Because Duetto is unsigned/unnotarized, Gatekeeper still blocks first launch — the
cask `caveats`, tap README, main README, and release notes all document the
right-click-Open / `xattr` workaround. Removing that friction later needs an Apple
Developer ID (sign + notarize in the build scripts); out of scope here.

## Deployment Plan
Already deployed (all live). To cut the **next** release with brew support:

1. Bump `<Version>` in `Directory.Build.props`, add a `CHANGELOG.md` entry.
2. `dotnet test -c Release` → all green.
3. Commit, tag `vX.Y.Z`, `git push origin main --tags`.
4. `VERSION=X.Y.Z scripts/publish-all.sh` (zips) and
   `VERSION=X.Y.Z scripts/make-dmg.sh osx-x64` then `… osx-arm64` (dmgs; arm64 last
   leaves `dist/Duetto.app` arm64 for local install).
5. `gh release create vX.Y.Z --title "Duetto vX.Y.Z" --notes-file <notes> dist/*.zip dist/*.dmg`.
6. `scripts/update-cask.sh X.Y.Z` → regenerates + pushes `Casks/duetto.rb` to the tap.
7. Users get it via `brew upgrade --cask duetto` (livecheck also picks up the new tag).
