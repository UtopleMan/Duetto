# Dark Mode

Add a Dark theme (plus Light and System) to Duetto, sourced from the Claude design
dark palette. The theme is chosen by a **config-file setting** and applied on the
next launch (restart-to-apply). No menu item.

> **Scope note / brief reversal:** the original `/real-work` brief said dark mode
> should be "selectable in the main menu bar." During brainstorming the user
> revised this to **a setting in the config file, no menu item**, and chose
> **restart-to-apply** (not live switching). This plan reflects the revision.
> The macOS `NativeMenu` and the Windows/Linux UI are left untouched.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Context (current state)

- App is hardcoded `RequestedThemeVariant="Light"` in `src/Duetto/App.axaml`.
- The UI's colors are **39 custom brushes** defined inline in `App.axaml`
  (`WindowBg`, `TextMid`, `TermBg`, …), referenced **227 times** across 9 `.axaml`
  files, all as `StaticResource` (zero `DynamicResource`). Fluent's own control
  chrome responds to `RequestedThemeVariant`, but these custom brushes do **not** —
  so a dark theme requires dark values for all 39 tokens.
- Config lives in one dir via `AppPaths` (`~/Library/Application Support/Duetto` on
  macOS, `$XDG_CONFIG_HOME/duetto` or `~/.config/duetto` on Linux, `%APPDATA%\Duetto`
  on Windows) as small JSON stores (`session.json`, `window.json`).
- `App.axaml.cs` creates `MainWindow` in `OnFrameworkInitializationCompleted`.

## Palette (from the Claude design)

Source: claude.ai/design project `9547189c-a040-4169-8fed-38dc0d79972e`, file
`Duetto File Manager.dc.html`, dark-mode turn. Canonical light↔dark tokens:

| token | light | dark |
|---|---|---|
| window | `#e8e6e1` | `#232220` |
| pane | `#ffffff` | `#2b2a27` |
| recessed | `#faf9f7` | `#26251f` |
| hairline | `#dad7d0` | `#383631` |
| text | `#33322c` | `#d8d5cc` |
| text-dim | `#7c7a70` | `#8f8d84` |
| accent | `#2f6fd0` | `#7fb0f5` |
| selection | `#dfe8f7` | `#5b9cf0` @22% (`#5B9CF038`) |
| folder | `#c8992f` | `#d9b45c` |
| terminal | `#26251f` | `#161511` |
| progress | `#2f8f5b` | `#2f8f5b` (unchanged) |

Extra dark render values observed in the artifact: file-mark `#6f6d66`,
selected-row text `#eaf1fc`, selected-row meta `#a9c3ea`, terminal input `#100f0c`,
graduated dark surfaces `#211f1a` / `#1c1b18` / `#2f2e28` / `#32302a` / `#33312d`.

Design note (verbatim): *"Selection, drawer and progress in the dark. Selected row
= translucent blue; command drawer inverts less (it was already dark); progress
green unchanged."* → terminal internals, danger, amber and green **carry over**.

### Full 39-brush Dark mapping (`src` = design token / carry-over / derived)

| App.axaml key | dark value | src |
|---|---|---|
| WindowBg | `#232220` | design: window |
| ChromeBg | `#1c1b18` | derived (chrome < window, mirrors light) |
| ToolbarBg | `#211f1a` | derived |
| PaneBg | `#2b2a27` | design: pane |
| RowHover | `#2f2e28` | derived (render hex) |
| ButtonHover | `#32302a` | derived (render hex) |
| Hairline | `#383631` | design: hairline |
| HairlineDark | `#302f2b` | derived |
| HairlineLight | `#33312d` | derived (render hex) |
| HairlinePale | `#2b2a27` | derived |
| InputBorder | `#383631` | design: hairline |
| TextPrimary | `#e6e3db` | derived (brightest text) |
| TextBody | `#d8d5cc` | design: text |
| TextMid | `#b4b1a8` | derived |
| TextDim | `#8f8d84` | design: text-dim |
| TextFaint | `#85837a` | derived |
| TextGhost | `#6f6d66` | design: file-mark tone |
| TextHint | `#5c5a53` | derived |
| HeaderText | `#8f8d84` | design: text-dim |
| Accent | `#7fb0f5` | design: accent |
| AccentDim | `#5f7290` | derived |
| SelectionBg | `#5B9CF038` | design: selection (22%) |
| ActivePathBg | `#5B9CF024` | derived (lighter selection) |
| ChipBg | `#5B9CF01F` | derived |
| ChipBorder | `#3a4a60` | derived |
| FolderMark | `#d9b45c` | design: folder |
| FileMark | `#6f6d66` | design: file-mark |
| Green | `#2f8f5b` | carry: progress |
| GreenLight | `#8fd0ab` | carry |
| SkipAmber | `#c69a3f` | derived (amber lifted for dark) |
| DangerRed | `#e5645c` | derived (red lifted for dark) |
| DangerText | `#e58079` | derived |
| DangerBg | `#3a2422` | derived (dark danger surface) |
| TermBg | `#161511` | design: terminal |
| TermInputBg | `#100f0c` | derived (render hex) |
| TermPrompt | `#7fd6a0` | carry |
| TermPath | `#8ca8d8` | carry |
| TermText | `#f0eeea` | carry |
| TermDim | `#6b695f` | carry |

The Light dictionary keeps today's exact values (verbatim move out of `App.axaml`).
Derived values are best-effort dark equivalents; Phase 1 Step 4 reconciles them
against the artifact's dark render before sign-off.

---

## Phase 1: Palette dictionaries (Light + Dark)
Status: Complete

- [x] Create `src/Duetto/Themes/Palette.Light.axaml` — a `ResourceDictionary`
      holding all 39 brushes with today's exact light values (moved verbatim from
      `App.axaml`).
- [x] Create `src/Duetto/Themes/Palette.Dark.axaml` — same 39 keys, dark values
      from the mapping table above.
- [x] Remove the 39 inline brush definitions from `App.axaml` (keep `MonoFont`,
      `FluentTheme`, and the `Button.toolbtn` styles). Merge `Palette.Light.axaml`
      by default so design-time / fallback stays light.
- [~] Reconcile the 12 `derived` rows against the artifact's dark-turn render —
      derived values set from the render-hex frequency scan; **final visual
      reconcile deferred to Phase 5 screenshot check.**
- [x] Add a parity test `tests/Duetto.Tests/Ui/PaletteParityTests.cs`: parse both
      `.axaml` files, assert the set of `x:Key` names is identical (Dark defines
      every key Light does, and vice versa).

### Verification Plan
- `dotnet build` → succeeds.
- `dotnet test --filter FullyQualifiedName~PaletteParityTests` → passes (identical
  key sets; a missing dark brush fails the test).

### Phase Summary
Done. `Palette.Light.axaml` (verbatim light values) + `Palette.Dark.axaml` (39 dark
values) created under `src/Duetto/Themes/`; both compile as AvaloniaResource (SDK
auto-glob, no csproj change needed). `App.axaml` now merges `Palette.Light.axaml`
via `ResourceInclude` instead of inline brushes; its 3 app-level style refs
(`TextMid`, `ButtonHover`, `TextHint`) became `DynamicResource` since they parse at
`App.Initialize()` — before the Phase 3 startup merge — so they must follow the
active palette dynamically. View-level `StaticResource` refs are untouched (views
parse after the startup merge). Build clean; parity test green; full suite 662 pass
(was 661), no regressions. Derived dark values (chrome/toolbar/hover/hairline
gradations, danger, chip, amber) are best-effort; reconcile visually in Phase 5.

## Phase 2: Theme setting persistence
Status: Complete

- [x] Add `enum AppTheme { System, Light, Dark }` (in `Duetto.Core.State`).
- [x] Add `AppPaths.SettingsJsonPath => Path.Combine(ConfigDir, "settings.json")`.
- [x] Add `ThemeSettingStore` (mirrors `SessionStore`): `AppTheme Load()` /
      `void Save(AppTheme)`, reading/writing `{ "theme": "System|Light|Dark" }`.
      Missing/corrupt file → `System`. Never throws.
- [x] Test `tests/Duetto.Tests/Core/ThemeSettingStoreTests.cs`: round-trip each
      value; missing file → `System`; corrupt JSON → `System` (no throw).

### Verification Plan
- `dotnet test --filter FullyQualifiedName~ThemeSettingStoreTests` → passes.

### Phase Summary
Done. `AppTheme{System,Light,Dark}` + `ThemeSettingStore` added to
`Duetto.Core.State` (mirrors `SessionStore`: injectable reader/writer, atomic
temp-then-move write, `JsonStringEnumConverter`, never throws). Unknown enum strings
throw `JsonException` → caught → `System`. `AppPaths.SettingsJsonPath` →
`<ConfigDir>/settings.json`. 6 tests pass (3 round-trip + missing + corrupt +
unknown-value).

## Phase 3: Startup wiring (restart-to-apply)
Status: Complete

- [x] Add pure resolver `ThemeResolver.Resolve(AppTheme setting, PlatformThemeVariant os)`
      → `(ThemeVariant variant, Uri paletteUri)`: `Light`→Light, `Dark`→Dark,
      `System`→follow `os`.
- [x] In `App` startup (`OnFrameworkInitializationCompleted`, before `MainWindow`):
      read the setting via `ThemeSettingStore`, resolve against
      `PlatformSettings.GetColorValues().ThemeVariant`, set `RequestedThemeVariant`,
      and append the resolved palette to `Resources.MergedDictionaries`. Headless
      runs default to System (→Light without OS signal); `--theme` overrides for
      screenshots.
- [x] Add `--theme system|light|dark` to `AppOptions` (`AppTheme? Theme`).
- [x] Test `tests/Duetto.Tests/Ui/ThemeResolverTests.cs`: the four resolution cases.

### Verification Plan
- `dotnet test --filter FullyQualifiedName~ThemeResolverTests` → passes.
- `dotnet test` (full suite) → all green, no regressions.

### Phase Summary
Done. `ThemeResolver.Resolve` (pure) maps setting+OS → `(ThemeVariant, palette Uri)`.
`App.ApplyTheme()` runs first in `OnFrameworkInitializationCompleted`: loads the
setting (`--theme` override wins; headless → System), resolves against
`PlatformSettings.GetColorValues().ThemeVariant`, sets `RequestedThemeVariant`, and
appends the palette dictionary (overrides the light default from App.axaml).
`--theme` added to `AppOptions`. 4 resolver tests pass; full suite **672** (was 662).
End-to-end verified by rendering `--theme light`/`--theme dark` screenshots: light is
unregressed, dark renders correctly (dark surfaces, lifted text, amber marks, green
command bar, translucent-blue selection) with no leaked light surfaces.

## Phase 4: Config-file UX + docs
Status: Complete

- [x] Document the setting: `docs/theme.md` — the `theme` key in `settings.json`,
      allowed values `System|Light|Dark`, applies on next launch, config-dir per OS.
- [x] Confirm no menu/UI code changed (macOS `NativeMenu` still only "About").

### Verification Plan
- `git grep -n "theme" src/Duetto/App.axaml` shows no new NativeMenuItem.
- Manual: set `"theme":"Dark"` in `<config>/settings.json`, relaunch, dark renders.

### Phase Summary
Done. `docs/theme.md` documents the `theme` key, values, restart-to-apply, and the
per-OS config path. `App.axaml`'s `NativeMenu` is unchanged (only "About Duetto") —
no theme menu item, matching the config-file-only decision.

## Phase 5: Full verification
Status: Complete

- [x] `dotnet build` → no errors.
- [x] `dotnet test` → all pass (incl. new PaletteParity / ThemeSettingStore /
      ThemeResolver tests). 672 total.
- [x] `dotnet format --verify-no-changes` on the new/changed `.cs` files → clean
      (repo has known pre-existing drift elsewhere — not touched).
- [x] Screenshot check both variants: `--theme light` unregressed; `--theme dark`
      renders dark surfaces/text/amber marks/green command bar/translucent-blue
      selection, no leaked light surfaces.

### Verification Plan
- Commands above; screenshots visually match the Claude design dark render.

### Phase Summary
All green. `dotnet build` 0 errors; `dotnet test` 672 pass; scoped `dotnet format`
clean; light + dark headless screenshots verified. Feature complete.

## Phase 6: Coverage — theme the leaked colors
Status: Complete

Screenshotting only the mac main window missed surfaces that don't flip: hardcoded
hex in views (Win/GNOME chrome, strips, pane chip, popover card) and hardcoded hex
in view models (marked-row fill, drive popover, transfer status, folder/file marks).

- [x] Replace literal colors in `MainWindow/CommandBar/PaneView/ProgressStrip/
      SimpleOperationStrip/SearchResultsView` axaml with palette brushes (White glyph
      on the red close-hover stays literal). Verified dark across mac/gnome/win chromes.
- [x] Add `PaletteLookup.Hex/Brush(key, lightFallback)` (resolves a palette key against
      the active theme; falls back to the light hex so light mode + headless tests are
      byte-identical) and route VM colors through it (`FileRow/Search.MarkColor`,
      `TransferViewModel` status, `DrivePopoverViewModel`, `BoolBrushConverters`).
- [x] Add `SuccessBg`/`SuccessBorder`/`DangerBorder` tokens (exit-code pill). Terminal
      output + saturated usage-bar colors stay literal (correct on dark already).

### Verification Plan
- `git grep -nE '="#[0-9A-Fa-f]{6}"' src/Duetto/Views` shows only the 2 `White` glyphs.
- `dotnet test` green (incl. `SharesPopoverTests.DotColor` exact-hex + parity at 42 keys).

### Phase Summary
Two leak layers fixed: 27 axaml literals → palette brushes (all 3 chromes verified
dark by screenshot), and 6 view-model color sites → `PaletteLookup`. Light values are
preserved byte-for-byte (most VM hexes already equalled a token; `PaletteLookup` falls
back to the light hex otherwise), so `SharesPopoverTests` and light rendering are
unchanged. Parity now 42 keys. Commits `8407856` (axaml), `cc135e0` (VM colors).

## Final Recap
Duetto now has Light / Dark / System themes sourced from the Claude design dark
palette, chosen via the `theme` key in `settings.json` and applied on next launch
(restart-to-apply — no menu item, per the revised brief).

Implementation:
- **Palette** split out of `App.axaml` into `Themes/Palette.Light.axaml` +
  `Themes/Palette.Dark.axaml` (39 brushes each; `PaletteParityTests` guards drift).
- **Setting** persisted by `ThemeSettingStore` → `AppPaths.SettingsJsonPath`
  (`AppTheme{System,Light,Dark}`, defaults to System, never throws).
- **Startup** `App.ApplyTheme()` resolves the setting (System follows the OS via
  `PlatformSettings`) with the pure `ThemeResolver`, sets `RequestedThemeVariant`,
  and appends the resolved palette over the light default.
- **CLI** `--theme system|light|dark` forces a variant (used for screenshots).
- **Docs** `docs/theme.md`.

Tests added: PaletteParity (1), ThemeSettingStore (6), ThemeResolver (4). Suite 662→672.
Commits: `8f3ff7a` (palette), `d3e5c68` (setting), `8fa0584` (startup), plus this
phase's docs.

Known follow-ups (out of scope here): 12 dark values are derived (not literal design
tokens) — screenshots look right but a pixel-level reconcile against the design could
refine chrome/toolbar/danger. Live switching (no restart) would need the 227
`StaticResource` refs converted to `DynamicResource`.

## Deployment Plan
Desktop app; ships in the next version.
1. `git push origin main` (feature commits).
2. Bump `Directory.Build.props` `<Version>` to `1.4.0` (new feature → minor) and add
   a CHANGELOG entry; commit `chore(release): v1.4.0 — dark mode`; tag `v1.4.0`; push
   commit + tag (matches the repo's release convention).
3. Rebuild + install locally: `VERSION=1.4.0 scripts/make-app-bundle.sh osx-arm64`,
   then copy `dist/Duetto.app` to `/Applications`.
4. Verify: set `"theme":"Dark"` in `~/Library/Application Support/Duetto/settings.json`,
   relaunch, confirm dark; set `"Light"`/remove, relaunch, confirm light.
