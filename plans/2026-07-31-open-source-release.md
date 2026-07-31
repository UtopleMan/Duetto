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
Status: Not started

- [ ] Add `LICENSE` (MIT, `Copyright (c) 2026 Peter`).
- [ ] Add root `global.json` pinning the .NET SDK (`{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}`) so CI resolves .NET 10.
- [ ] Add root `Directory.Build.props` with shared `<Version>1.0.0</Version>`,
      `<Product>Duetto</Product>`, `<Company>` and `<Authors>` so assemblies carry
      a version; verify both csproj inherit it.
- [ ] Parameterize `scripts/make-app-bundle.sh`: accept a target RID (default
      `osx-arm64`) and read a `VERSION` env var (default `1.0.0`) for
      `CFBundleShortVersionString` / `CFBundleVersion`; publish that RID.
- [ ] Parameterize `scripts/make-dmg.sh` similarly (RID + VERSION, output
      `dist/Duetto-<version>-<arch>.dmg`).
- [ ] Extend `scripts/publish-all.sh` to include `osx-x64` and stamp `-p:Version=$VERSION`;
      name zips `duetto-<version>-<rid>.zip`.
- [ ] Add `CHANGELOG.md` with a `1.0.0` entry summarizing current features.

### Verification Plan
- `test -f LICENSE && grep -q "MIT" LICENSE && echo LICENSE-OK`
- `grep -q "1.0.0" Directory.Build.props && echo VERSION-OK`
- `dotnet build src/Duetto/Duetto.csproj -c Release -p:Version=1.0.0 2>&1 | grep -qi "Build succeeded" && echo BUILD-OK`
- `VERSION=1.0.0 bash scripts/make-app-bundle.sh osx-arm64 >/dev/null 2>&1 && /usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" dist/Duetto.app/Contents/Info.plist` → prints `1.0.0`

### Phase Summary
_(write when phase completes)_

---

## Phase 2: README + interim screenshots
Status: Not started

- [ ] Add `scripts/screenshots/capture-chrome.sh`: runs the app headless with
      `--chrome win|mac|gnome --screenshot` against a curated sample folder,
      writing `docs/screenshots/{windows,macos,linux}.png`. (Reproducible; stored
      for reuse. Real-OS versions replace these in Phase 6.)
- [ ] Run it; commit the three PNGs under `docs/screenshots/`.
- [ ] Write `README.md`: hero (name + one-line tagline + app icon), badges
      (MIT license, CI status, latest release, downloads), a concise **Features**
      list (dual-pane, keyboard-driven, SFTP remote browsing/transfer, live
      search, cross-platform Trash, `duetto` CLI + folder arg, remembers window +
      pane folders, native-feeling per-OS chrome), the three screenshots, an
      **Install** section (per-platform download links to the latest release +
      macOS Gatekeeper note + `duetto` CLI note), **Build from source**, and
      **License**.
- [ ] Add `CONTRIBUTING.md` (build/test instructions) and a short repo
      description/topics list to set on GitHub in Phase 4.

### Verification Plan
- `bash scripts/screenshots/capture-chrome.sh && ls docs/screenshots/windows.png docs/screenshots/macos.png docs/screenshots/linux.png` → all three exist, non-zero size
- `grep -qi "## Features" README.md && grep -qi "MIT" README.md && echo README-OK`
- `python3 -c "import re,sys; sys.exit(0 if all(x in open('README.md').read() for x in ['docs/screenshots/windows.png','docs/screenshots/macos.png','docs/screenshots/linux.png']) else 1)" && echo IMGS-LINKED`

### Phase Summary
_(write when phase completes)_

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
