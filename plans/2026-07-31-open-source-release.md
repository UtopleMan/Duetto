# Open-source Duetto: license, README, CI/release pipeline, v1.0.0 downloads

Turn Duetto into a public open-source project on `github.com/UtopleMan/duetto`:
MIT license, a simple beautiful README with per-platform screenshots, a
tag-driven release pipeline producing downloadable builds for Windows, Linux and
macOS (Intel + Apple Silicon), and a first `v1.0.0` release. Real-VM screenshots
are a later phase; the core ships first.

## Locked decisions
- **License:** MIT. Copyright holder line: `Copyright (c) 2026 Peter` (adjust if a
  different holder/org is preferred).
- **Repo:** public `github.com/UtopleMan/duetto` (gh authed as `UtopleMan`).
- **Version:** start at `v1.0.0`; tag-driven (`v*` tag → release).
- **macOS distribution:** unsigned `.dmg` + README Gatekeeper instructions (no
  Apple Developer ID).
- **Targets:** `win-x64` zip, `linux-x64` zip, `osx-arm64` zip+dmg, `osx-x64`
  zip+dmg.
- **CI:** `dotnet test` on push/PR; release build+publish on `v*` tag.
- **Screenshots:** real per-OS screenshots (user requirement). macOS + Linux
  captured locally (native / Docker+Xvfb); Windows via a stored script the user
  runs on a real Windows box. Core README ships first with interim built-in
  `--chrome` renders, swapped for real ones in Phase 6.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary**; run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**. Do NOT run Phase 4/5
(public repo + release) without the user's go-ahead — they are outward-facing.

Key context: .NET 10 (`10.0.301` local), Avalonia 11.3.18 desktop app. App
already supports `--chrome win|mac|gnome`, `--screenshot <png>`, `--smoke`,
`--headless`, and a positional folder arg. Build scripts live in `scripts/`
(`make-app-bundle.sh` = one macOS `.app`; `make-dmg.sh`; `publish-all.sh` = 3-RID
zips). `.gitignore` already excludes `bin/ obj/ dist/`. 502 tests pass.

---

## Phase 1: Repo scaffolding & versioning
Status: Complete

- [x] Add `LICENSE` (MIT, `Copyright (c) 2026 UtopleMan`).
- [x] Add root `global.json` pinning the .NET SDK (`10.0.100`, `rollForward: latestFeature`) so CI resolves .NET 10.
- [x] Add root `Directory.Build.props` with shared `<Version>1.0.0</Version>`,
      `<Product>`, `<Company>`, `<Authors>`, `<Copyright>`; both csproj inherit it.
- [x] Parameterize `scripts/make-app-bundle.sh`: RID arg (default `osx-arm64`) +
      `VERSION` env (default `1.0.0`) stamped into `CFBundle*Version`.
- [x] Parameterize `scripts/make-dmg.sh` (RID + VERSION → `dist/Duetto-<version>-<arch>.dmg`).
- [x] Extend `scripts/publish-all.sh` to include `osx-x64`, stamp `-p:Version`, name
      zips `duetto-<version>-<rid>.zip`.
- [x] Add `CHANGELOG.md` with a `1.0.0` entry.

### Verification Plan
- `test -f LICENSE && grep -q "MIT" LICENSE && echo LICENSE-OK`
- `grep -q "1.0.0" Directory.Build.props && echo VERSION-OK`
- `dotnet build src/Duetto/Duetto.csproj -c Release -p:Version=1.0.0 2>&1 | grep -qi "Build succeeded" && echo BUILD-OK`
- `VERSION=1.0.0 bash scripts/make-app-bundle.sh osx-arm64 >/dev/null 2>&1 && /usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" dist/Duetto.app/Contents/Info.plist` → prints `1.0.0`

### Phase Summary
Done. Added `LICENSE` (MIT, © 2026 UtopleMan), `global.json` (SDK 10.0.100
latestFeature — resolves to local 10.0.301 and lets CI's setup-dotnet pick .NET
10), `Directory.Build.props` (Version 1.0.0 + Product/Company/Authors/Copyright,
inherited by all three projects), and `CHANGELOG.md` (1.0.0 feature list). Build
scripts are now parameterized by **RID + `VERSION` env**: `make-app-bundle.sh`
and `make-dmg.sh` take an RID (default `osx-arm64`) and stamp the version;
`make-dmg.sh` emits `dist/Duetto-<version>-<arch>.dmg`; `publish-all.sh` now
covers **osx-arm64, osx-x64, win-x64, linux-x64** and names zips
`duetto-<version>-<rid>.zip`. All backward-compatible (no-arg calls still build
osx-arm64 @ 1.0.0). Verified: `dotnet --version` 10.0.301 under the pin,
LICENSE-OK, VERSION-OK, Release build succeeded, bundle Info.plist reads
`1.0.0`, full suite still **502 passed**. Next: Phase 2 (README + screenshots).

---

## Phase 2: README + interim screenshots
Status: Complete

- [x] Add `scripts/screenshots/capture-chrome.sh`: builds the app, generates a
      curated sample `$HOME` tree at `/tmp/duetto-demo`, and captures
      `docs/screenshots/{windows,macos,linux}.png` via `--chrome win|mac|gnome
      --screenshot` (left pane = `Projects`, right pane = home). Reproducible.
- [x] Run it; commit the three PNGs under `docs/screenshots/`.
- [x] Write `README.md`: centered hero (icon + tagline + MIT/CI/release/downloads
      badges), one-line intro, the three labelled screenshots, a concise
      **Features** list, **Install** (per-platform download table + macOS
      Gatekeeper note + `duetto` CLI note), **Build from source**, **License**.
- [x] Add `CONTRIBUTING.md`; preserve the old detailed SFTP/config/security docs
      into `docs/remote-sftp.md` (linked from README).

### Verification Plan
- `bash scripts/screenshots/capture-chrome.sh && ls docs/screenshots/windows.png docs/screenshots/macos.png docs/screenshots/linux.png` → all three exist, non-zero size
- `grep -qi "## Features" README.md && grep -qi "MIT" README.md && echo README-OK`
- `python3 -c "import re,sys; sys.exit(0 if all(x in open('README.md').read() for x in ['docs/screenshots/windows.png','docs/screenshots/macos.png','docs/screenshots/linux.png']) else 1)" && echo IMGS-LINKED`

### Phase Summary
Done. `scripts/screenshots/capture-chrome.sh` reproducibly renders all three
platform shots headlessly: it builds the app, lays down a tidy sample home tree
at `/tmp/duetto-demo` (Documents/Downloads/Pictures/Projects + realistic files
with fixed mtimes), points the app at it via `$HOME` so both panes and the GNOME
Places rail show clean content, and captures one offscreen frame per `--chrome`
mode. Output committed to `docs/screenshots/{windows,macos,linux}.png`. New
`README.md` is the simple/beautiful version the user asked for: centered hero
(app icon + tagline + 4 badges), the three screenshots, a tight Features list,
an Install table (win-x64, macOS arm64+Intel dmg/zip, linux-x64) with the macOS
Gatekeeper `xattr` note and `duetto` CLI usage, Build-from-source, and License.
The previous README's valuable SFTP setup / config-location / security-caveat
content was preserved into `docs/remote-sftp.md` and linked, rather than lost.
Added `CONTRIBUTING.md`. Note: the GNOME shot's bottom bar shows the dev
machine's `user@host` (macOS reads it from the OS, not env — not overridable
here); harmless and replaced by the real Linux VM capture in Phase 6. All
verification checks pass. Next: Phase 3 (CI + release workflows).

---

## Phase 3: CI + release workflows
Status: Not started

- [ ] `.github/workflows/ci.yml`: on `push` + `pull_request` → `ubuntu-latest`
      (and a `macos-latest` job so the macOS-only trash tests run): setup-dotnet
      from `global.json`, `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj -c Release`.
- [ ] `.github/workflows/release.yml`: on `push` tag `v*` → `macos-latest`:
      derive `VERSION` from the tag (`v1.2.3`→`1.2.3`); build `win-x64`,
      `linux-x64`, `osx-arm64`, `osx-x64` self-contained single-file zips
      (cross-RID `dotnet publish`), and `osx-arm64`/`osx-x64` `.dmg` via the
      parameterized bundle scripts; create a GitHub Release for the tag and upload
      all six assets (zips + dmgs). Use `GITHUB_TOKEN` + `gh release create` or
      `softprops/action-gh-release`.
- [ ] Add a build-status badge target and release/download badges to README.
- [ ] Validate both workflow YAMLs parse.

### Verification Plan
- `python3 -c "import yaml,glob; [yaml.safe_load(open(f)) for f in glob.glob('.github/workflows/*.yml')]; print('YAML-OK')"`
- `grep -q "on:" .github/workflows/release.yml && grep -q "v\*" .github/workflows/release.yml && echo TAG-TRIGGER-OK`
- Local dry-run of the cross-publish for one non-mac RID: `dotnet publish src/Duetto -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:Version=1.0.0 -o /tmp/dz >/dev/null 2>&1 && test -f /tmp/dz/Duetto && echo LINUX-PUBLISH-OK`

### Phase Summary
_(write when phase completes)_

---

## Phase 4: Publish repo to GitHub  ⚠️ outward-facing — needs user go-ahead
Status: Not started

- [ ] `gh repo create UtopleMan/duetto --public --source . --remote origin --description "<desc>" --disable-wiki` (do NOT auto-push yet).
- [ ] Push `main`: `git push -u origin main`.
- [ ] Set repo topics (file-manager, avalonia, dotnet, cross-platform, sftp, macos, windows, linux).
- [ ] Confirm the CI workflow triggers and passes on the pushed commit.

### Verification Plan
- `gh repo view UtopleMan/duetto --json visibility,name -q '.visibility+" "+.name'` → `PUBLIC duetto`
- `gh run list --repo UtopleMan/duetto --workflow ci.yml --limit 1 --json conclusion -q '.[0].conclusion'` → `success` (after the run finishes)

### Phase Summary
_(write when phase completes)_

---

## Phase 5: Cut v1.0.0 release  ⚠️ outward-facing — needs user go-ahead
Status: Not started

- [ ] Tag and push: `git tag v1.0.0 && git push origin v1.0.0`.
- [ ] Watch the release workflow to green.
- [ ] Verify the GitHub Release `v1.0.0` exists with all six downloadable assets.
- [ ] Download one asset and sanity-check it (zip unpacks / dmg mounts).
- [ ] Update README download links to point at the `v1.0.0` (and `latest`) release.

### Verification Plan
- `gh run list --repo UtopleMan/duetto --workflow release.yml --limit 1 --json conclusion -q '.[0].conclusion'` → `success`
- `gh release view v1.0.0 --repo UtopleMan/duetto --json assets -q '.assets|length'` → `6`
- `gh release download v1.0.0 --repo UtopleMan/duetto --pattern '*linux-x64*' -D /tmp/rel && unzip -tq /tmp/rel/*linux-x64*.zip && echo ASSET-OK`

### Phase Summary
_(write when phase completes)_

---

## Phase 6: Real per-OS screenshots
Status: Not started

- [ ] `scripts/screenshots/capture-macos.sh`: launch the installed/built app and
      capture a real macOS window screenshot (`screencapture`), write
      `docs/screenshots/macos.png`. Run it now (native).
- [ ] `scripts/screenshots/capture-linux.sh`: Docker + `Xvfb` running the
      `linux-x64` self-contained build under a virtual X display, capture with
      `import`/`scrot`/`xwd`, write `docs/screenshots/linux.png`. Run it now.
      (Container = real Linux userland; note it is not a full VM.)
- [ ] `scripts/screenshots/capture-windows.ps1`: PowerShell script for a real
      Windows machine — downloads/extracts the `win-x64` release, launches it,
      screenshots the window, writes `docs/screenshots/windows.png`. Document how
      to run it. **Deferred:** user runs it on a Windows box and supplies the PNG
      (no Windows virtualization available on this arm64 host).
- [ ] Replace the interim README screenshots with the real macOS + Linux captures;
      swap in the real Windows one when supplied. Commit; (optionally) tag a
      follow-up release if images ship in-repo only.

### Verification Plan
- `bash scripts/screenshots/capture-macos.sh && file docs/screenshots/macos.png | grep -qi "PNG image" && echo MAC-SHOT-OK`
- `bash scripts/screenshots/capture-linux.sh && file docs/screenshots/linux.png | grep -qi "PNG image" && echo LINUX-SHOT-OK`
- `test -f scripts/screenshots/capture-windows.ps1 && echo WIN-SCRIPT-PRESENT`

### Phase Summary
_(write when phase completes)_

---

## Final Recap
_(write when all phases complete)_

## Deployment Plan
_(write when all phases complete)_
