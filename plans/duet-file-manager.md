# Duet — two-pane file manager (Avalonia / C#)

Build Duet v1 from the Claude design spec "Duet File Manager.dc.html" (project
9547189c-a040-4169-8fed-38dc0d79972e on claude.ai/design): an orthodox two-pane
file manager for Windows/macOS/Linux with a scoped recursive search, a bottom
shell command bar with an output drawer, non-modal copy progress, and one shared
layout wrapped in three per-OS chromes.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Locked decisions (user-confirmed 2026-07-26)
- Solution name **Duetto**, app name **Duet**. Repo root `/Users/dude/Sources/UtopleMan/duetto`, git-initialized in Phase 1.
- .NET 9, Avalonia 11, CommunityToolkit.Mvvm (source generators). Tests: xunit + Avalonia.Headless.
- **In scope:** two panes + file ops (copy/move/delete/rename/new folder), progress strip, command bar + output drawer, scoped recursive search.
- **Out of scope:** remote (SFTP/S3/SMB), tabs per pane, dark theme, overwrite-conflict dialog.
- Delete goes to **OS Trash** (macOS Trash / Recycle Bin / freedesktop trash), not permanent.
- Copy conflicts: **auto-skip when destination has same name and is newer**, count shown in progress strip, "Review skipped" lists them. No dialog.
- Three **RID-locked chromes**: win-x64 → 1a "Unified Slate", osx-arm64 → 1b "Paper Panes", linux-x64 → 1c "Rail" (with Places sidebar). `--chrome win|mac|gnome` CLI override for previewing any chrome on any OS.
- Deliverables: `dotnet publish` self-contained single-file per RID (osx-arm64, win-x64, linux-x64) **plus a macOS `Duet.app` bundle**.
- Shell for command bar: `$SHELL -c` (fallback `/bin/sh`) on Unix, `cmd.exe /c` on Windows. Runs in active pane's cwd.
- Keyboard: F5 copy, F6 move, F7 new folder, F8 delete, Tab switch pane, Ctrl/Cmd+F focus search, Esc clears search / closes drawer, Enter open, Backspace/↑-button go up.
- Panes auto-refresh via FileSystemWatcher. Column headers click-sortable.
- No CI (no git remote yet).

## Design tokens (from spec — no need to re-fetch it)
- Palette: window bg `#faf9f7`/`#f6f5f2`, chrome `#f0eeea`, hairline `#e2dfd8`/`#dcd9d2`, text `#22211d`/`#33322c`, dim text `#7c7a70`/`#8c8a80`, faint `#a8a69c`/`#c2bfb5`.
- Accent blue `#2f6fd0` (focus, selection, active), selection row bg `#dfe8f7`, active path bar bg `#e9eef8` (mac variant `#eef2fa`), chip bg `#eef1f7` border `#dbe3f2`.
- Folder mark amber `#c8992f`, file mark `#b6b3a8` (11×11 rounded squares, radius 2.5px). Progress/success green `#2f8f5b` (light `#8fd0ab`), skipped amber `#b08020`, danger `#d94040`/`#a03c3c`.
- Terminal: bg `#26251f` (input row `#1c1b16`), prompt green `#7fd6a0`, path blue `#8ca8d8`, text `#f0eeea`, dim `#6b695f`.
- Type: OS system font for UI; IBM Plex Mono (bundle it) for paths, sizes, perms, chips, command bar. Row height 27px, file rows 12.5px text, headers 10.5px uppercase letter-spacing .05em, path bars 11px mono, status bars 26px high / 11px text.
- Columns: Name (flex) | Size 74px right mono | Type 88px | Modified 112px | Perms/Access right mono. Column gap 14px, row padding 0 12px.
- Active pane = tinted path bar (`#e9eef8`, blue mono path, "ACTIVE" tag in Win chrome); inactive = `#f0eeea`/`#f6f5f2` gray path bar. Never a border.
- Search field: leading `⌕`, mono scope chip ("in shipyard/"), placeholder "Search everything below this folder…", dim shortcut hint right-aligned; focused = 1.5px blue border + `0 0 0 3px rgba(47,111,208,.13)` ring. Filter chips: Names / + Contents / Any size / Any date (active chip = blue tint).
- Search results mode: right pane header `#e9eef8` "Results for “query” · below <dir> · <time>"; columns Name | Folder 200px mono | Size 70px | Modified 96px; rows 29px; footer hint "Enter reveals in left pane".
- Progress strip (1f): sits between panes and command bar, `#f3f1ed`, title "Copying to <dst>", current file + speed mono, Pause (gray) / Cancel (red tint) buttons, 7px two-tone green bar (done + in-flight), footer "62 of 148 files done · 3 skipped — same name, newer at destination · Review skipped". Per-file status column appears in both panes during copy: done/42%/queued/skipped (green/blue/gray/amber) on source; ok/writing/newer on destination, in-flight file shown as `name.part`.
- Command bar idle (Win/1a): 40px `#f0eeea` strip, green mono `cwd $`, dim placeholder, right-aligned dim hints "F5 copy · F6 move · F8 delete · Tab switch pane" that brighten on hover. GNOME/1c: dark `#26251f` strip with `user@host cwd $`. Mac/1b: white rounded card "cwd ❯".
- Output drawer (1e): opens above the prompt, light header (command, `exit 0 · 12.4 s` green pill, Copy output, "Esc close", drag handle), dark `#26251f` body with colored mono lines, max-height then scroll, input row stays below.
- Chromes: 1a Win = square window, 34px title bar (blue 12px app mark, "Duet", dim mono context), — ▢ ✕ caption buttons (✕ hover red), 46px toolbar (← → ↑, search, New / ⋯), flush panes split by 1px hairline. 1b mac = native traffic lights, centered title "left · right", floating toolbar row (30px rounded buttons, pill search field), panes as separate white rounded-9px cards with shadow on `#e8e6e1` desk, command bar its own card. 1c GNOME = 46px header bar with round buttons and centered title/subtitle stack, 186px Places rail (`#f3f1ed`, amber folder dots, Places + Remote sections), panes flush, dark command strip, round-cornered window.
- Empty-state/status bar text: "14 items", "1 selected — 9.2 KB", "12 items — 6.7 GB".

## Architecture (target)
```
Duetto.sln
├─ src/Duet.Core/          net9.0 class lib — no Avalonia reference
│   ├─ FileSystem/         DirectoryLister, FileEntry, EntrySorter, FormatUtil (sizes/dates)
│   ├─ Operations/         TransferEngine (copy/move w/ progress+skip), TrashService (per-OS), FileOps (rename/mkdir)
│   ├─ Search/             SearchService (recursive, names/contents, cancellable, streaming)
│   └─ Shell/              ShellRunner (process exec, stdout/stderr capture, history)
├─ src/Duet/               Avalonia app
│   ├─ ViewModels/         MainViewModel, PaneViewModel, CommandBarViewModel, SearchViewModel, TransferViewModel
│   ├─ Views/              MainWindow + PaneView, ColumnHeader, StatusBar, CommandBar, OutputDrawer, ProgressStrip, SearchField
│   ├─ Chrome/             IChrome + WinChrome/MacChrome/GnomeChrome (title bar, toolbar arrangement, pane framing, Places rail)
│   └─ Assets/             IBM Plex Mono fonts, app icon
└─ tests/Duet.Tests/       xunit; Core unit tests + Avalonia.Headless UI tests
```
Chrome resolution: default from `OperatingSystem.IsWindows()/IsMacOS()/IsLinux()`, overridden by `--chrome win|mac|gnome` argument.

## Phase 1: Repo + solution scaffold
Status: Complete

- [x] `git init` in repo root; `.gitignore` for .NET (bin/obj/publish/.DS_Store)
- [x] Create solution with `src/Duet.Core` (classlib), `src/Duet` (Avalonia app), `tests/Duet.Tests` (xunit, refs both)
- [x] Add packages: Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Diagnostics, CommunityToolkit.Mvvm, Avalonia.Headless.XUnit in tests
- [x] Bundle IBM Plex Mono Regular/Medium/SemiBold/Bold TTFs in `src/Duet/Assets/Fonts`, register as embedded font family
- [x] `Program.cs` parses `--chrome`; empty MainWindow shows and closes cleanly
- [x] Initial commit

### Verification Plan
- `dotnet build Duetto.slnx` → succeeds, 0 warnings-as-errors
- `dotnet test` → passes (placeholder test)
- `dotnet run --project src/Duet -- --smoke` renders window then exits 0 within ~1 s (no `timeout` cmd on this Mac — background + `kill -0` poll)

### Phase Summary
Done 2026-07-26, commit 77e65fd. **Deviations from original plan (all environment-driven):**
- Target framework is **net10.0**, not net9.0 — machine has only .NET SDK/runtime 10.0.301; net9.0 would not run locally.
- Solution file is **`Duetto.slnx`** (SDK 10 `dotnet new sln` emits the new XML format), not `.sln`.
- Avalonia pinned to **11.3.18** across all Avalonia packages. Avalonia 12.1.0 exists on NuGet but Avalonia.Diagnostics tops out at 11.3.18; mixing majors breaks. CommunityToolkit.Mvvm 8.4.2.
- App entry: `AppOptions.Parse` handles `--chrome win|mac|gnome` (default from `OperatingSystem.Is*`) and `--smoke` (closes 400 ms after `Opened` via `DispatcherTimer.RunOnce`, exit 0). Options exposed as `Program.Options`.
- Fonts: IBM Plex Mono 4 weights from google/fonts (OFL), resource key `MonoFont` in `App.axaml` (`avares://Duet/Assets/Fonts#IBM Plex Mono`).
- `Duet.csproj` uses compiled bindings by default (`AvaloniaUseCompiledBindingsByDefault=true`).

## Phase 2: Core domain (Duet.Core, test-first)
Status: Complete

- [x] `FileEntry` (name, full path, isDir, size, type label, modified, unix perms string + Win RW summary) and `DirectoryLister.List(path)` (hidden files included; permission errors non-fatal)
- [x] `EntrySorter` — sort by any column asc/desc, dirs always grouped first
- [x] `FormatUtil` — human sizes, date formats, type labels, unix perms string
- [x] `FileOps` — rename, new folder ("New folder", "New folder 2", …)
- [x] `TrashService` — macOS: move into `~/.Trash` w/ uniquified name; Linux: freedesktop Trash/files + info/.trashinfo; Windows: `SHFileOperationW` FOF_ALLOWUNDO
- [x] `TransferEngine` — per-file + total progress, `.part` then atomic rename + mtime copy, pause/resume/cancel, auto-skip dest-same-name-newer, overwrite dest-older, skipped list with reason; move deletes source files then empty dirs depth-first
- [x] `SearchService` — recursive, name substring + optional contents (≤4 MB, NUL-sniff binary skip, chunk-overlap match), dir-name matches too, streaming via Channel/IAsyncEnumerable, cancellable, skips unreadable dirs, live SearchStats
- [x] `ShellRunner` — `$SHELL -c` / `cmd.exe /c`, streams tagged stdout/stderr lines, exit code + duration, history w/o consecutive dupes
- [x] xunit tests for all of the above against temp directories (no mocks of the real FS)

### Verification Plan
- `dotnet test --filter "FullyQualifiedName~Duet.Tests.Core"` → all pass
- Trash test on macOS: create temp file, `TrashService.Trash`, assert gone from source + present in `~/.Trash`

### Phase Summary
Done 2026-07-26. 40/40 tests green (`dotnet test --filter FullyQualifiedName~Duet.Tests.Core`). API surface for the UI phases:
- `DirectoryLister.List(path)` → `IReadOnlyList<FileEntry>`; `EntrySorter.Sort(entries, SortColumn, ascending)`; `FormatUtil.{HumanSize,DateLong,DateShort,TypeLabel}`.
- `TransferEngine.Start(sourcePaths, destDir, TransferMode)` → `TransferSession` with `Changed` event (worker thread! marshal to dispatcher), `Snapshot()` (immutable `TransferSnapshot`), `Pause/Resume/Cancel`, `StatusOf(sourcePath)`, `Completion` task. Skip reason constant `TransferEngine.SkipReasonNewer`.
- `TrashService.Trash(path)` → trashed path (null on Windows).
- `SearchService.Search(scopeDir, query, includeContents, SearchStats, ct)` → `IAsyncEnumerable<SearchHit>` (`Entry` + `RelativeFolder`); stats update live from worker thread.
- `new ShellRunner().RunAsync(command, cwd, onLine, ct)` → `ShellResult(ExitCode, Duration)`; `onLine` fires on threadpool threads.
- Gotcha fixed en route: macOS `pwd` returns `/private/var/...` for `/var/...` temp paths — tests compare leaf names.

## Phase 3: Panes UI — shared layout
Status: Complete

- [x] `PaneViewModel`: current dir, entries (sorted), selection (multi: Cmd/Ctrl-click toggle, Shift-click range), cursor, navigation history (back/forward/up), status line text ("N items", "n selected — size")
- [x] `MainViewModel`: left/right panes, active pane tracking, Tab switches
- [x] `PaneView`: path bar (tinted when active per tokens), column header row (click sorts, arrow indicator), virtualized 27px rows (mark square, name ellipsis, mono size, type, modified, perms), status bar
- [x] Row interactions: double-click / Enter opens dir or launches file with OS default app; Backspace and ↑ toolbar go up; typing letters jumps to match (type-ahead)
- [x] Toolbar per shared layout: ← → ↑ history buttons, search field (visual only this phase: icon, scope chip bound to active dir, placeholder, shortcut hint), New (folder) button
- [x] Keyboard: F2 rename (inline edit), F7 new folder, arrows/PageUp/Down/Home/End cursor movement
- [x] FileSystemWatcher per pane → debounced reload preserving selection/cursor
- [x] Neutral chrome for now (Win-like flush layout); chrome polish deferred to Phase 7
- [x] Headless UI tests: open dir shows entries; Tab moves active tint; Enter descends; sort toggles; rename works

### Verification Plan
- `dotnet test` → all pass (incl. new headless tests)
- `dotnet run --project src/Duet -- --smoke` exits 0
- Manual: `dotnet run --project src/Duet`, navigate repo root, check 27px rows, active-pane tint follows Tab

### Phase Summary
Done 2026-07-26, commit 4419007. 49/49 tests green, smoke exit 0. Key facts for later phases:
- `PaneViewModel.Selection` is an Avalonia `SelectionModel<FileRowViewModel>` with `Source = Rows` set in the ctor (required for VM-level tests; ListBox adopts the same instance via `Selection="{Binding Selection}"`).
- All pane keyboard handling lives in `MainWindow.OnPreviewKeyDown` (tunnel handler) acting on `Vm.ActivePane`, NOT in PaneView — headless focus is unreliable and orthodox managers route keys to the active pane anyway. Guarded by `IsTextInputFocused()` (skips when a TextBox has focus). Currently: Tab switch, Enter open, Backspace up, F2 rename, F7 new folder, printable chars → `PaneView.TypeAhead`.
- Active pane switches on pointer-press/focus via `PaneView.Interacted` event wired in MainWindow.
- Row template: custom `ListBoxItem` ControlTheme (27px, hover #f2f0ec, selected #dfe8f7); columns grid `*,14,74,14,88,14,112,14,76`; last column shows unix perms (or "RW" on Windows) and swaps to `TransferStatus` badge when set (Phase 4 hooks: `FileRowViewModel.TransferStatus`/`TransferStatusColor`).
- Inline rename: TextBox in row template, focus-on-attach, Enter commit/Esc cancel/LostFocus commit.
- Headless tests MUST use Skia: `TestAppBuilder` = `.UseSkia().UseHeadless(new(){UseHeadlessDrawing=false})`, else embedded IBM Plex Mono fails glyph creation.
- Sort headers are VM computed props (`NameHeader` etc.) with ▲/▼ suffix.

## Phase 4: File operations UI + progress strip
Status: Complete

- [x] F5 copy / F6 move selected entries from active pane to other pane's dir via `TransferEngine`; F8/Delete → TrashService with no dialog (trash is undoable)
- [x] `ProgressStrip` between panes and command bar per tokens: title, current file + throughput, Pause/Cancel, two-tone bar, "x of y files done · n skipped — same name, newer at destination · Review skipped"
- [x] Per-file status column in panes during transfer (done/%/queued/skipped; dest shows `.part` writing row)
- [x] "Review skipped" opens flyout listing skipped files + reason
- [x] Window title/subtitle reflects operation in mac/GNOME chromes later; for now strip only
- [x] App remains fully interactive during transfer (engine on background task, UI updates via dispatcher)
- [x] Headless tests: copy N files updates strip counts; conflict file skipped and listed; cancel stops mid-set

### Verification Plan
- `dotnet test` → pass
- Manual: copy a big folder between temp dirs; strip shows progress, pause/cancel work, skipped review lists conflicts

### Phase Summary
Done 2026-07-26. 54/54 tests green. Notes:
- `TransferViewModel` polls `Session.Snapshot()` on a 100 ms DispatcherTimer (`UpdateNow()` public for deterministic tests). Auto-dismisses 1.5 s after completion when nothing was skipped; otherwise Cancel button becomes "Dismiss" and the strip stays for review. `Dismissed` event clears row badges and reloads both panes.
- Two-tone progress bar implemented with star-width Grid columns updated in `ProgressStrip` code-behind (`UpdateBar`); no custom drawing.
- `TransferSession.StateOf(path)` added to Duet.Core for per-row percent badges.
- Strip is docked *after* the command bar in MainWindow's DockPanel so it renders above it (DockPanel outermost-first ordering).
- Delete (F8/Del) trashes synchronously — instant for the move-to-trash implementations used.

## Phase 5: Command bar + output drawer
Status: Complete

- [x] `CommandBar` per tokens: green mono `cwd-name $` prompt (basename of active pane dir), input, dim hover-brightening hints (F5/F6/F8/Tab)
- [x] Enter runs via `ShellRunner` in active pane cwd; drawer opens above prompt: header (command, exit pill green/red with duration, Copy output, Esc close, drag handle), dark body with streamed lines, autoscroll, max-height ~50% window then scroll
- [x] ↑/↓ cycles history; Esc closes drawer (first press) / clears input (second); panes refresh after command exits
- [x] Focus rules: command bar focus doesn't steal pane keyboard nav — clicking prompt or typing into it focuses; Tab still switches panes when list focused
- [x] Headless tests: run `echo hello` → drawer shows "hello", exit 0 pill; failing command shows nonzero exit; history recall

### Verification Plan
- `dotnet test` → pass
- Manual: `git status --short`, `ls -la` from bar; output styled per spec; Esc closes

### Phase Summary
Done 2026-07-26. 60/60 tests green, smoke exit 0. Notes:
- `CommandBarViewModel(cwdProvider)` owned by MainViewModel; `CommandFinished` reloads both panes. `RunAsync` streams lines via `Dispatcher.UIThread.Post` (tests must `RunJobs()` before asserting Output).
- Esc semantics: first press closes drawer, second clears input (`Escape()`); handled in `CommandBar.OnInputKeyDown`, which also does Enter=run, ↑/↓=history.
- Exit pill colors via `BoolBrushConverters` (FuncValueConverter statics referenced with `{x:Static vm:BoolBrushConverters.*}`).
- Drawer body max-height 260, autoscroll on CollectionChanged; stderr lines amber #d9b45c, stdout #d8d5cc.
- MainWindow's `IsTextInputFocused()` guard keeps pane keys (incl. type-ahead) from firing while the command TextBox has focus.

## Phase 6: Scoped recursive search
Status: Complete

- [x] Ctrl/Cmd+F focuses search field; scope chip always shows active pane folder; typing starts incremental search (debounced) below that folder
- [x] Right pane switches to results mode per tokens (header "Results for …" + elapsed, Name/Folder/Size/Modified columns, live count "18 matches in 1,204 files"); left pane untouched
- [x] Filter chips: Names (default) / + Contents toggle; Any size / Any date chips with simple menu (size: >1 MB, >100 MB…; date: today, this week, this month) — post-filter on results
- [x] Enter on a result reveals it in left pane (navigate + select); Esc clears search and restores right pane's previous dir; "Open as pane" pins results as right pane listing
- [x] Results are actionable: F5/F6/F8 work on selected results
- [x] Headless tests: search temp tree by name finds nested file; contents toggle finds text match; Esc restores; reveal-in-left navigates

### Verification Plan
- `dotnet test` → pass
- Manual: search "axaml"-like pattern in a real tree, verify streaming count, reveal, Esc restore

### Phase Summary
Done 2026-07-26. 68/68 tests green. Notes:
- `SearchViewModel(scopeProvider)`: 300 ms debounce on Query/filters; `StartSearchAsync()` public for tests. Results stream on the UI thread via `await foreach` over `SearchService.Search`. Scope captured from ActivePane at search start.
- Results overlay the RIGHT pane (`Panel` in MainWindow grid col 2, `IsVisible="{Binding IsActive}"`), left pane untouched; reveal navigates LEFT pane + selects + activates it (design 1d).
- F5/F6/F8 branch in MainViewModel: when `Search.IsActive`, they act on `Search.SelectedEntries` with destination = Left pane dir (no row badges — TransferViewModel accepts null source pane).
- "Open as pane" = `PinResults()`: cancels streaming, sets `IsPinned`, clears query; empty-query search skips teardown while pinned; Esc/`Clear()` unpins.
- Key routing (MainWindow preview): Cmd/Ctrl+F focuses SearchBox (before text-input guard); Esc → clear search, else close drawer; Enter → RevealSelected while searching. SearchBox-local: Esc clears + refocuses pane, Enter reveals first/selected result.
- Filter chips styled `Border.chip`/`Button.chipbtn` (+`.active`); size/date via MenuFlyout click handlers.

## Phase 7: Three chromes + packaging
Status: Not started

- [ ] `IChrome` abstraction (window decorations mode, title bar content, toolbar composition, pane framing, command bar skin, optional Places rail)
- [ ] `WinChrome` (1a): custom title bar 34px with app mark + caption buttons (— ▢ ✕, red close hover), flush panes + hairline, light command strip with F-hints
- [ ] `MacChrome` (1b): native title bar w/ traffic lights (ExtendClientAreaToDecorationsHint), centered "left · right" title, floating rounded toolbar, panes as shadowed cards on `#e8e6e1` desk, command bar card with `❯`
- [ ] `GnomeChrome` (1c): 46px header bar (round buttons, title/subtitle stack), 186px Places rail (Home/Documents/Downloads/Pictures/Trash + volumes; amber dots; navigates active pane), dark `#26251f` command strip with `user@host path $`, search filter chips row
- [ ] RID default + `--chrome` override wired; all three render correctly via override on macOS
- [ ] Publish: `dotnet publish -c Release -r {osx-arm64|win-x64|linux-x64} --self-contained -p:PublishSingleFile=true` profiles; trimming only if Avalonia-safe, else skip
- [ ] `scripts/make-app-bundle.sh` builds `dist/Duet.app` (Info.plist, icns generated from simple blue-square mark, osx-arm64 binary), plus zips of all three binaries in `dist/`
- [ ] Headless render test per chrome (instantiate each chrome, assert key elements present)

### Verification Plan
- `dotnet run --project src/Duet -- --chrome win --smoke` / `--chrome gnome --smoke` / `--chrome mac --smoke` all exit 0
- All three publish commands succeed; `file dist/*/Duet` shows correct arch; `dist/Duet.app` launches via `open dist/Duet.app`
- `dotnet test` full suite green

### Phase Summary
_(write when phase completes)_

## Phase 8: Polish + final verification
Status: Not started

- [ ] Sweep every visual token against the spec table above (colors, sizes, spacing, fonts) in all three chromes
- [ ] Empty dir, permission-denied dir, very long names (ellipsis), 10k-file dir (virtualization smooth)
- [ ] All keyboard paths from Locked Decisions work; hints hover-brighten
- [ ] Full test suite + smoke on all chromes; record results
- [ ] Final commit; write Final Recap + Deployment Plan below

### Verification Plan
- `dotnet test` green; three `--smoke` runs exit 0; publish artifacts rebuilt clean

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan
_(write when all phases complete: step-by-step deployment instructions)_
