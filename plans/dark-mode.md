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
Status: Not started

- [ ] Add pure resolver `ThemeResolver.Resolve(AppTheme setting, PlatformThemeVariant os)`
      → `(ThemeVariant variant, string paletteUri)`: `Light`→Light, `Dark`→Dark,
      `System`→follow `os`.
- [ ] In `App` startup (before `MainWindow` is created): read the setting via
      `ThemeSettingStore`, resolve against
      `PlatformSettings.GetColorValues().ThemeVariant`, set
      `RequestedThemeVariant`, and merge the resolved palette dictionary into
      `Application.Resources.MergedDictionaries` (so `StaticResource` lookups bind
      the chosen palette). Gate off the store in headless/screenshot/smoke runs
      (respect `Program.Options`), matching how `SessionStore`/window store are gated.
- [ ] Test `tests/Duetto.Tests/Ui/ThemeResolverTests.cs`: the four resolution cases
      (Light→Light, Dark→Dark, System+osLight→Light, System+osDark→Dark) map to the
      right variant + palette uri.

### Verification Plan
- `dotnet test --filter FullyQualifiedName~ThemeResolverTests` → passes.
- `dotnet test` (full suite) → all green, no regressions.

### Phase Summary
_(write when phase completes)_

## Phase 4: Config-file UX + docs
Status: Not started

- [ ] Document the setting: a short `docs/theme.md` (or a README section) — the
      `theme` key in `settings.json`, allowed values `System|Light|Dark`, and that
      it applies on next launch. Reference the config-dir location per OS.
- [ ] Confirm no menu/UI code changed (macOS `NativeMenu` still only "About").

### Verification Plan
- `git grep -n "theme" src/Duetto/App.axaml` shows no new NativeMenuItem.
- Manual: set `"theme":"Dark"` in `<config>/settings.json`, relaunch, dark renders.

### Phase Summary
_(write when phase completes)_

## Phase 5: Full verification
Status: Not started

- [ ] `dotnet build` → no errors.
- [ ] `dotnet test` → all pass (incl. new PaletteParity / ThemeSettingStore /
      ThemeResolver tests).
- [ ] `dotnet format --verify-no-changes` on the new/changed files (repo has known
      pre-existing drift elsewhere — do not reformat unrelated files).
- [ ] Screenshot check both variants: launch with `--screenshot` under `theme=Light`
      and `theme=Dark`; confirm dark surfaces/text/selection match the design and no
      element stays light (leaked hardcoded color).

### Verification Plan
- Commands above; screenshots visually match the Claude design dark render.

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete)_

## Deployment Plan
_(write when all phases complete)_
