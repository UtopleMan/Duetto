# F3 File Preview

Add a keyboard-driven file viewer to Duetto: `F3` on the cursor file (pane or search
results) opens a reusable, modeless viewer window that renders text, a hex dump, or an
image, over every backend (local, SFTP, SMB, S3, Azure) through the existing
`IFileSystemProvider` seam.

## Sequencing
Run `plans/2026-09-01-avalonia-12-upgrade-and-rich-preview.md` **Phase 1 (Avalonia 12 +
NuGet upgrade) before this plan**, so a broken test is attributable to one change or the
other. That plan's Phases 2–4 then extend the types built here with `Vector` (SVG) and
`Pdf` preview kinds.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Agreed Design (decisions already made — do not re-litigate)

| Decision | Choice |
| --- | --- |
| Shell | Separate `ViewerWindow`, **single reusable instance**, modeless, native chrome (like `ConnectWindow`/`AboutWindow`) |
| Content | Text, hex dump, image — auto-detected. No manual mode switch, no encoding picker in v1 |
| Byte source | Fetch eagerly through `IFileSystemProvider.OpenRead` into memory (no temp file); **partial** fetch for text/hex, **full** fetch for images below the image cap |
| Budgets | Text/hex: 4 MiB. Image: 64 MiB (above it, fall back to hex of the first 4 MiB). Sniff window: 8 KiB |
| Truncation | Footer notice `first 4 MB of 812 MB` + an **Open in default app** action |
| Long lines | Kept whole; horizontal scrolling, no clipping |
| Loading UX | Viewer window opens immediately in a `Loading…` state; `Esc` cancels the fetch and closes. Runs off the UI thread, independent of `MainViewModel.ActiveOperation`, so copy/delete stay usable |
| Features (v1) | Line numbers, word-wrap toggle, find-in-file (`Ctrl`/`Cmd+F`, `n`/`N`) |
| Entry points | `F3` on the active pane's cursor row; `F3` on the highlighted search result. No toolbar button, no marked-set stepping |
| Out of scope | Editing (`F4`), syntax highlighting, chunked "load more", encoding picker, PDF/office/video preview, marked-set navigation |

### Assumptions (flag to the user if any turn out wrong)
- Image bytes are decoded from a `MemoryStream` via Avalonia `Bitmap`; no temp file is
  written for previewing. The temp-file path (`RemoteFileOpener`) is used only by
  **Open in default app**, which already exists.
- Encoding detection: BOM first (UTF-8 / UTF-16 LE / UTF-16 BE), otherwise UTF-8. A
  head containing `NUL` or invalid UTF-8 means hex mode. The header shows the label.
- Find highlights and scrolls to the **matching line** (row-level), not the matching
  character range.
- The viewer never writes to files; it opens read streams only.

## Phase 1: Core preview engine
Status: Complete

New folder `src/Duetto.Core/Preview/`, no Avalonia dependency, fully unit-testable.

- [x] Add `PreviewKind` enum (`Text`, `Hex`, `Image`, `Empty`) in `src/Duetto.Core/Preview/PreviewKind.cs`
- [x] Add `PreviewLimits` record (`TextBudgetBytes = 4 * 1024 * 1024`, `ImageMaxBytes = 64 * 1024 * 1024`, `SniffBytes = 8 * 1024`) with a `static PreviewLimits Default` in `src/Duetto.Core/Preview/PreviewLimits.cs`
- [x] Add `ContentSniffer.Detect(ReadOnlySpan<byte> head, long totalBytes, PreviewLimits limits)` in `src/Duetto.Core/Preview/ContentSniffer.cs`: zero length → `Empty`; PNG/JPEG/GIF/BMP/WEBP magic bytes and `totalBytes <= ImageMaxBytes` → `Image`; BOM → `Text`; `NUL` byte or invalid UTF-8 in head → `Hex`; otherwise `Text`
- [x] Add `TextEncodingDetector.Detect(ReadOnlySpan<byte> head)` returning `(Encoding Encoding, string Label, int BomLength)` — `UTF-8`, `UTF-8 (BOM)`, `UTF-16 LE`, `UTF-16 BE`
- [x] Add `HexDump.Format(ReadOnlySpan<byte> bytes, long startOffset)` in `src/Duetto.Core/Preview/HexDump.cs` producing 16-bytes-per-line rows `00000000  89 50 4E 47 0D 0A 1A 0A  00 00 00 0D 49 48 44 52  |.PNG........IHDR|` (non-printable → `.`)
- [x] Add `PreviewContent` record in `src/Duetto.Core/Preview/PreviewContent.cs`: `Kind`, `Lines` (`IReadOnlyList<string>`, text or hex rows), `ImageBytes` (`byte[]?`), `EncodingLabel`, `TotalBytes`, `LoadedBytes`, `IsTruncated`
- [x] Add `PreviewLoader` in `src/Duetto.Core/Preview/PreviewLoader.cs` taking a `FileSystemRegistry`: `PreviewContent Load(string fullAddress, CancellationToken ct, PreviewLimits? limits = null)` — resolve provider, `Stat` for total size, read at most `budget + 1` bytes from `OpenRead` (the extra byte proves truncation), sniff, then build text lines / hex rows / image bytes
- [x] Text line building: strip the BOM, decode with the detected encoding, split on `\n`, drop a trailing `\r`, keep full line length, drop a trailing empty final line
- [x] Cancellation: `ct.ThrowIfCancellationRequested()` inside the read loop; no partial content is returned on cancel
- [x] Tests in `tests/Duetto.Tests/Core/PreviewLoaderTests.cs` and `tests/Duetto.Tests/Core/ContentSnifferTests.cs` using `TempDir` (local) and `InMemoryFileSystemProvider` + `FileSystemRegistry.Register("sftp", "srv", fs)` (remote):
  - [x] UTF-8 text file → `Text`, correct lines, `IsTruncated == false`, label `UTF-8`
  - [x] UTF-8 BOM and UTF-16 LE files → correct lines and labels, BOM not present in line 1
  - [x] File with an embedded `NUL` → `Hex`, first row matches the expected dump format
  - [x] PNG magic bytes under the image cap → `Image` with `ImageBytes` equal to the whole file
  - [x] PNG magic bytes over `ImageMaxBytes` → `Hex`
  - [x] File larger than `TextBudgetBytes` → `IsTruncated == true`, `LoadedBytes == TextBudgetBytes`, `TotalBytes` = real size
  - [x] Empty file → `Empty`
  - [x] Remote (`sftp://srv/note.txt`) file loads through the registry with identical results
  - [x] Pre-cancelled token → `OperationCanceledException`

### Verification Plan
- `dotnet build Duetto.slnx` — succeeds with 0 errors, 0 new warnings
- `dotnet test --filter "FullyQualifiedName~Preview"` — all new Core tests pass
- `dotnet test` — full suite still green (no regressions vs. the pre-change baseline count)

### Verification Results (2026-09-01)
- `dotnet build Duetto.slnx` — 0 errors, warning set unchanged from the Avalonia 12 baseline.
- `dotnet test --filter "FullyQualifiedName~Preview|FullyQualifiedName~ContentSniffer|FullyQualifiedName~HexDump"` — **33 passed, 0 failed**.
- `dotnet test` — **772 passed, 0 failed, 0 skipped** (739 pre-change + 33 new).

### Phase Summary

Seven new files under `src/Duetto.Core/Preview/`, three new test files. No Avalonia reference added to Core; the whole engine is plain BCL.

**Read strategy (matters for Phases 2-4).** `PreviewLoader.Load` opens the stream once and reads in three steps: `SniffBytes` for the head, then the kind decides how much more. Images read to `ImageMaxBytes`; text and hex read to `TextBudgetBytes + 1`, where that extra byte — not `Stat`'s size — is what proves truncation. Stale or absent remote sizes therefore cannot produce a wrong `IsTruncated`. Reads go through a shared 64 KiB-chunk `Read` helper that checks the cancellation token every chunk.

**Design choices not spelled out in the plan**
- `PreviewKind.Empty` is decided by an **empty head**, not by `Stat` reporting zero. A remote provider that reports size 0 for a file with content still previews correctly.
- **BMP needs more than its magic.** `"BM"` is two bytes and would misfire on any text file starting with `BM` ("BMW is a car"). `IsBmp` additionally requires the little-endian DWORD at offset 2 to equal `totalBytes`. Cost: a BMP written with a zero size field previews as hex instead of an image. There is a regression test for the text case (`Text_starting_with_bm_is_not_mistaken_for_bmp`).
- **UTF-8 validation tolerates a cut sequence.** The 8 KiB sniff window can land mid-codepoint. `IsValidUtf8` uses `Utf8.ToUtf16(..., isFinalBlock: false)` and accepts `Done` *or* `NeedMoreData`, so a truncated multi-byte sequence at the window edge does not demote a UTF-8 file to hex. Only `InvalidData` means hex.
- **UTF-16 without a BOM is hex, by design.** It is full of `NUL` bytes and the agreed design is BOM-first-otherwise-UTF-8.
- `EncodingLabel` is `""` for every kind except `Text`.
- The image path sets `IsTruncated = false` always — an image is either loaded whole or it was never classified as an image.
- `Load` throws `FileNotFoundException` when `Stat` returns null and `NotSupportedException` for a directory. Phase 2's `ViewerViewModel` already lists both in its catch set.

**Hex row layout** (fixed 60-char prefix, so Phase 2 can rely on monospace alignment):
`{offset:X8}` + 2 spaces + 8 bytes as `XX` space-joined (23 cols) + 2 spaces + next 8 bytes (23 cols) + 2 spaces + `|ascii|`. Short rows pad the hex columns with spaces; the ASCII column is not padded. Bytes and offsets are uppercase hex; printable is `0x20..0x7E`.

**For Phase 2:** construct with `new PreviewLoader(registry)`. `MainViewModel` already owns a `FileSystemRegistry` — reuse that instance so remote previews resolve against live connections rather than a fresh registry.

## Phase 2: Viewer window and view model
Status: Complete

- [x] Add `ViewerViewModel` in `src/Duetto/ViewModels/ViewerViewModel.cs` (`ObservableObject`): observable `FileName`, `AddressText`, `HeaderText`, `EncodingLabel`, `SizeText`, `TruncationText`, `IsLoading`, `ErrorText`, `Kind`, `IsWrapped`, `ObservableCollection<PreviewLineViewModel> Lines`, `Bitmap? Image`, `ImageDimensionsText`
- [x] Add a `LoadScheduler` seam matching the `MainViewModel.OpenScheduler` pattern (`Func<Action<CancellationToken>, CancellationToken, Task>`, default `Task.Run`) plus a `Task LoadCompletion` so headless tests can run the load synchronously
- [x] `ViewerViewModel.Show(string fullAddress, string displayName)`: cancel any in-flight load, reset state to `IsLoading`, run `PreviewLoader.Load` through the scheduler, then marshal results onto the UI thread; catch `IOException`, `UnauthorizedAccessException`, `FileNotFoundException`, `NotSupportedException`, `SshException`, `SocketException`, `InvalidOperationException`, `HostKeyChangedException` into `ErrorText`
- [x] Decode `ImageBytes` to an Avalonia `Bitmap` from a `MemoryStream`; a decode failure falls back to hex rows of the first `TextBudgetBytes`, not to an error
- [x] Add `PreviewLineViewModel` (`Number`, `Text`, `IsMatch`) — line numbers are 1-based; hex rows show no gutter number
- [x] Add `OpenInDefaultAppRequested` event on the view model plus an `OpenInDefaultAppCommand` (`RelayCommand`), always available, surfaced next to the truncation notice
- [x] Add `src/Duetto/Views/ViewerWindow.axaml` + `.axaml.cs`: `Width="900" Height="640"`, `CanResize="True"`, `WindowStartupLocation="CenterOwner"`, palette `StaticResource` brushes only (light/dark parity), native title bar
- [x] Window layout: header row (name · size · encoding/kind), content area (`ListBox` of `PreviewLineViewModel` for text/hex with `MonoFont`, virtualized, `ScrollViewer.HorizontalScrollBarVisibility="Auto"`; `Image` with `Stretch="Uniform"` for image mode; centered `Loading …` and error states), footer row (truncation notice, `Open in default app` link, key hints)
- [x] Key handling in `ViewerWindow` (`KeyDown`): `Esc` and `F3` close (cancelling an in-flight load); arrows / `PageUp` / `PageDown` / `Home` / `End` scroll the list
- [x] Headless UI tests in `tests/Duetto.Tests/Ui/ViewerTests.cs`:
  - [x] `Show` on a text file fills `Lines` with numbered rows and sets the encoding label
  - [x] `Show` on a binary file yields hex rows
  - [x] `Show` on a PNG sets `Image` and `ImageDimensionsText`
  - [x] `Show` on a truncated file sets `TruncationText` mentioning both loaded and total size
  - [x] Unreadable path sets `ErrorText` and leaves `IsLoading == false`
  - [x] `Show` twice reuses one view model and replaces content (no leaked rows from the first file)
  - [x] `OpenInDefaultAppCommand` raises `OpenInDefaultAppRequested` with the address

### Verification Plan
- `dotnet build Duetto.slnx` — succeeds
- `dotnet test --filter "FullyQualifiedName~Viewer"` — all viewer view-model tests pass
- `dotnet test --filter "FullyQualifiedName~PaletteParity"` — theme parity test still passes (any new brush key exists in both `Palette.Light.axaml` and `Palette.Dark.axaml`)

### Verification Results (2026-09-01)
- `dotnet build Duetto.slnx` — 0 errors, no new warnings (including no `AVLN` XAML warnings).
- `dotnet test --filter "FullyQualifiedName~Viewer|FullyQualifiedName~PaletteParity"` — **14 passed, 0 failed**.

### Phase Summary

Five new files: `ViewerViewModel`, `PreviewLineViewModel`, `ViewerConverters` (view models), `ViewerWindow.axaml` + `.axaml.cs`. One brush key added to both palettes.

**Loading flow.** `Show(address, displayName)` cancels any in-flight load, resets every observable to its blank state, sets `IsLoading`, and assigns `LoadCompletion = RunLoadAsync(...)`. `RunLoadAsync` awaits `LoadScheduler` (default `Task.Run`, overridable to run synchronously in tests) and resumes on the UI thread through Avalonia's synchronisation context — the same shape `MainViewModel.RunOpenAsync` already uses.

**Stale-result guard.** After the await, `RunLoadAsync` returns early unless `ReferenceEquals(_cts, cts)`. A second `Show` therefore cannot have its content overwritten by a slower first load. `Second_show_replaces_the_first_file_content` covers the ordering.

**Decode failure is verified, not guessed.** A truncated PNG makes `new Bitmap(stream)` throw **`System.ArgumentException`** ("Unable to load bitmap from provided data") — confirmed with a throwaway probe test rather than assumed. `TryDecode` catches exactly that and nothing more; anything else is a real fault and should surface. On failure the viewer switches to `Hex` over the first `TextBudgetBytes` of the image (never the full 64 MiB, which would be millions of rows) and sets its own truncation notice.

**View-state properties are derived, not stored.** `IsTextMode` / `IsImageMode` / `IsEmptyFile` / `HasError` / `HasTruncation` / `ShowLineNumbers` are computed getters kept fresh by `[NotifyPropertyChangedFor]` on `IsLoading`, `ErrorText` and `Kind`. That keeps "which panel is visible" in one place instead of scattered across the XAML.

**Two view details worth knowing**
- The `ItemTemplate` reaches the window's view model with `{Binding $parent[ListBox].((vm:ViewerViewModel)DataContext).IsWrapped}` — the compiled-binding cast is required, and this pattern is used for both `IsWrapped` (wrap toggle) and `ShowLineNumbers` (gutter visibility). It compiles clean under `AvaloniaUseCompiledBindingsByDefault`.
- `MatchHighlight` (`#f6e6a8` light / `#5c4a1e` dark) was added to both palettes now, though nothing sets `IsMatch` until Phase 4. `ViewerConverters.MatchBackground` is already wired to the row background.

**Deviation from the plan's catch list.** The plan listed `FileNotFoundException` alongside `IOException` in `RunLoadAsync`. `FileNotFoundException` derives from `IOException`, so the entry was redundant; the filter keeps `IOException` and drops it.

**For Phase 3:** `ViewerWindow` takes a `ViewerViewModel` in its constructor and closes on `Esc`/`F3` after calling `Vm.Cancel()`. Build the view model with `new ViewerViewModel(mainVm.Registry)` so remote previews resolve against live connections. `OpenInDefaultAppRequested` carries the full address string.

## Phase 3: F3 wiring, single reusable window, placement
Status: Complete

- [x] Add `sealed record PreviewRequest(string Address, string DisplayName)` and an `Action<PreviewRequest> OpenViewer { get; set; } = _ => { }` seam on `MainViewModel` (mirrors the existing `OpenConnectDialog` seam)
- [x] Add `MainViewModel.PreviewCursor()`: when `Search.IsActive` and a result is selected, build the address with `PathUtil.ToAddress(Search.ScopeDir, entry.FullPath)`; otherwise use `ActivePane.CursorRow` with `PathUtil.ToAddress(ActivePane.CurrentPath, row.Entry.FullPath)`. Do nothing for directories, `..` rows, or an empty selection
- [x] Route the search-results case only when the results list actually holds focus, so `F3` in a pane never previews a stale search hit
- [x] Add `case Key.F3:` to `MainWindow.OnPreviewKeyDown` (after the existing `IsTextInputFocused()` guard so an inline rename swallows it) calling `Vm.PreviewCursor()` and marking the event handled
- [x] In `MainWindow`, hold a single `ViewerWindow?` field: create + `Show(this)` on first request, reuse and `Activate()` afterwards, null the field on `Closed`, and close it when `MainWindow` closes
- [x] Add `AppPaths.ViewerWindowJsonPath` (`viewer-window.json`) and persist the viewer's size/position with the existing `WindowPlacementStore` + `IsVisibleOn` screen check, following `MainWindow.WirePlacement`/`RestorePlacement`
- [x] Wire `OpenInDefaultAppRequested`: local addresses go to `PaneViewModel.LaunchFile`; remote addresses reuse the existing remote-open path (`RemoteFileOpener` + progress strip + the `ActiveOperation` gate)
- [x] Keep the viewer out of the `ActiveOperation` gate: previewing while a copy/delete runs must stay possible
- [x] Tests in `tests/Duetto.Tests/Ui/PreviewKeyTests.cs`:
  - [x] `PreviewCursor` on a file row invokes `OpenViewer` with the pane-qualified address
  - [x] `PreviewCursor` on a directory row or `..` invokes nothing
  - [x] `PreviewCursor` on a remote pane produces an `sftp://srv/...` address
  - [x] `PreviewCursor` with active search + focused results previews the selected hit's address
  - [x] Preview while `ActiveOperation` is unfinished still invokes `OpenViewer`
  - [x] `F3` key on `MainWindow` reaches `PreviewCursor` (headless key-press test, following `RenameKeyTests`)

### Verification Plan
- `dotnet build Duetto.slnx` — succeeds
- `dotnet test --filter "FullyQualifiedName~PreviewKey"` — wiring tests pass
- `dotnet test` — full suite green
- Manual smoke (`dotnet run --project src/Duetto`): `F3` on a text file, on a PNG, on a binary, on a folder (nothing happens), and on a remote file after connecting a share

### Verification Results (2026-09-01)
- `dotnet build Duetto.slnx` — 0 errors, no new warnings.
- `dotnet test --filter "FullyQualifiedName~PreviewKey"` — **12 passed, 0 failed**.
- `dotnet test` — **797 passed, 0 failed, 0 skipped** (739 baseline + 33 + 13 + 12).
- Manual smoke: **not done interactively.** Nobody drove a real window by hand. The headless tests do press `F3` on `MainWindow` and assert the viewer opens, is reused, closes with the main window, and launches the default app — but "a human saw a PNG render in a real window" is still outstanding. Deferred to the Phase 4 run.

### Phase Summary

**Two seams on `MainViewModel`, both defaulting to no-ops so the view model stays headless-testable:**
- `Action<PreviewRequest> OpenViewer` — mirrors the existing `OpenConnectDialog` seam. `MainWindow` assigns it to `ShowViewer`.
- `Func<bool> SearchResultsFocused` — the view model cannot see focus, so `MainWindow` assigns it to the pre-existing `ResultsHaveFocus()`. This is what keeps `F3` in a pane from previewing a stale search hit while results are merely visible. Two tests pin both directions.

**`PreviewCursor` / `PreviewTarget`** are a command/query pair: `PreviewTarget` decides and returns `PreviewRequest?`, `PreviewCursor` fires the seam. Directories, `..` rows, and an empty selection all return null. Search hits use `PathUtil.ToAddress(Search.ScopeDir, ...)`, pane rows use `PathUtil.ToAddress(ActivePane.CurrentPath, ...)`, so remote panes yield `sftp://srv/note.txt`.

**Refactor: `StartRemoteFileOpen` split.** The viewer's "Open in default app" needs an address, not a `FileRowViewModel`. The row-shaped method now delegates to a new `StartRemoteOpen(address, name)`, and a new public `OpenAddressInDefaultApp(address)` picks local (`ActivePane.LaunchFile`) vs remote (`StartRemoteOpen`) via `PathUtil.IsRemote`. The local/remote decision therefore lives in the view model where a test can reach it, not in `MainWindow`.

**`ActiveOperation` gating is deliberately asymmetric.** `PreviewCursor` does **not** check `ActiveOperation` — previewing during a copy or delete is the whole point, and `Preview_is_not_gated_by_a_running_operation` pins it. `StartRemoteOpen` **keeps** its gate, because that path downloads to a temp file and owns the progress strip.

**Single reusable window.** `MainWindow._viewer` is created lazily by `CreateViewer()`, nulled on the window's `Closed`, and closed from `MainWindow.OnClosed`. `internal ViewerWindow? Viewer` exposes it to tests (`InternalsVisibleTo Duetto.Tests` already existed). The viewer's view model is built with `new ViewerViewModel(Vm.Registry)` — the live registry, so remote previews resolve against open connections.

**Placement.** `AppPaths.ViewerWindowJsonPath` (`viewer-window.json`). `ViewerWindow` carries its own `WirePlacement` / `RestorePlacement` / `RecordNormalBounds` triple, deliberately duplicating `MainWindow`'s rather than extracting a shared binder: this is the *second* occurrence, and `AGENTS.md` says duplicate once, extract on the third. If a third window ever needs placement, extract a `WindowPlacementBinder` then and convert both. Placement is skipped in headless mode, same as `MainWindow`.

**Small DRY win taken along the way:** the `Screens.All → ScreenBounds` projection was inline in `MainWindow`'s constructor and needed a second caller, so it became `MainWindow.ScreenBoundsProvider()`.

**Escape handling note for Phase 4.** `ViewerWindow.OnKeyDown` currently closes on `Esc` or `F3` unconditionally, and `OnClosing` calls `Vm.Cancel()` so an in-flight load is cancelled however the window closes. Phase 4 must make `Esc` close the *find box* first and only close the window on a second press.

## Phase 4: Word wrap, find-in-file, docs
Status: Complete

- [x] Word wrap: `IsWrapped` toggles `TextWrapping` on the line rows; bound to the `W` key and a footer toggle; wrap state persists for the session (not to disk)
- [x] Find state on `ViewerViewModel`: `FindQuery`, `IsFindOpen`, `MatchCount`, `CurrentMatchIndex`, `MatchPositionText` (`3 of 27`), `FindNext()`, `FindPrevious()`, `CloseFind()`
- [x] Matching is case-insensitive substring over the loaded lines; matched rows set `IsMatch` for a highlight brush and the current match is scrolled into view
- [x] `Ctrl+F` / `Cmd+F` opens and focuses the find box; `Enter` / `n` next, `Shift+Enter` / `N` previous, `Esc` closes the find box first and only closes the window on a second press
- [x] Find is hidden in image mode
- [x] Tests in `tests/Duetto.Tests/Ui/ViewerFindTests.cs`:
  - [x] Query matching 3 lines sets `MatchCount == 3` and marks exactly those rows
  - [x] `FindNext` wraps from the last match back to the first; `FindPrevious` wraps backwards
  - [x] Query with no match sets `MatchCount == 0` and marks nothing
  - [x] Changing the previewed file clears find state
  - [x] Wrap toggle flips `IsWrapped` and survives a find
- [x] Docs: add `F3` to the README keyboard list and feature bullets; add a CHANGELOG entry under a new version heading; tick the viewer item in `plans/backlog.md` (add it if absent) with a pointer to this plan

### Verification Plan
- `dotnet build Duetto.slnx` — succeeds
- `dotnet test` — full suite green
- `grep -n "F3" README.md CHANGELOG.md` — both mention the viewer
- Manual smoke: open a large log, confirm the truncation footer, run a find across it, toggle wrap, press `Esc` twice

### Verification Results (2026-09-02)
- `dotnet build Duetto.slnx --no-incremental` — 0 errors. Warning set identical to the Avalonia 12 baseline: CS4014 x6, xUnit1031 x4, CA1416 x4, xUnit2031 x2, MVVMTK0034 x2. No `AVLN` XAML warnings.
- `dotnet test` — **812 passed, 0 failed, 0 skipped** (739 baseline + 33 + 13 + 12 + 15).
- `dotnet test --filter "FullyQualifiedName~ViewerFind"` — **15 passed, 0 failed**.
- `grep -n "F3" README.md CHANGELOG.md` — both hit.
- `dotnet list package --vulnerable --include-transitive` — clean on all three projects.
- **Visual smoke, done headlessly.** A throwaway probe drove the real `ViewerWindow` through `HeadlessWindowExtensions.CaptureRenderedFrame` and rendered four PNGs, which were inspected: a 60-line log in text mode (line numbers, mono font, `server.log · 3.4 KB · UTF-8` header, `first 1.2 KB of 3.4 KB` footer with the budget deliberately lowered to force truncation); the same log with find open on `WARN` (three rows highlighted amber, `1 of 3`, find bar laid out correctly); a PNG in image mode (`2 × 2` dimensions, find bar and Wrap button correctly hidden); and a 96-byte binary in hex mode (aligned dump with the ASCII gutter). The probe was deleted afterwards — no test-only code shipped.
- **Interactive smoke: not done.** Nobody pressed `F3` in a real window on a real desktop. Keyboard paths are covered by headless key-press tests (`F3` open/reuse, `Ctrl+F`, `n`/`N`, `W`, double-`Esc`), and layout by the captures above, but a human-driven run is still outstanding — as is `F3` on a live remote share, which is exercised only through `InMemoryFileSystemProvider`.

### Phase Summary

**Find is a plain substring scan over already-loaded lines** — no regex, no incremental search, no background work. Everything is in memory by the time find runs (at most `TextBudgetBytes`), so `Rematch` is a single `Contains(query, OrdinalIgnoreCase)` pass that sets `PreviewLineViewModel.IsMatch` and records matching line indices in `_matches`. Retyping the query re-runs the whole scan; at 4 MiB that is cheap enough to skip any incremental machinery.

**`_matches` holds line indices, not view models.** `StepMatch` does modular arithmetic over that list, so `FindNext` from the last match wraps to the first and `FindPrevious` wraps backwards, and `ScrollToLineRequested` carries the *line index* — which is exactly what `ListBox.ScrollIntoView` wants. The window subscribes once in its constructor; the view model never touches a control.

**`CurrentMatchIndex` is 0-based with `-1` meaning "none"**, and `MatchPositionText` renders `"" / "no matches" / "3 of 27"` from it. Stepping with zero matches is a no-op rather than an error.

**Two find flags, not one.** `IsFindOpen` is what the keyboard toggles; `IsFindVisible` (`IsFindOpen && IsTextMode`) is what the XAML binds. That way opening find, then previewing an image, hides the bar without losing the user's intent — and `OpenFind()` simply refuses to set the flag when the current preview is not text.

**Keyboard split across two handlers.**
- `ViewerWindow.OnKeyDown` handles `Ctrl`/`Cmd+F` *first* (so it works even from inside the find box), then bails out if a `TextBox` has focus — the same `FocusManager?.GetFocusedElement() is TextBox` guard `MainWindow` uses. Only after that guard do `n` / `N` / `W` / `Esc` / `F3` apply. `Typing_in_the_find_box_does_not_trigger_the_window_shortcuts` pins this.
- `OnFindBoxKeyDown` handles `Enter` / `Shift+Enter` / `Esc` inside the box and marks them handled, so they never reach the window handler.
- `Esc` closes the find box when it is visible and closes the window otherwise — two presses to leave, as specified.

**Wrap is session state, never persisted.** `Show()` deliberately does not reset `IsWrapped`, so it survives previewing a different file (`Wrap_survives_showing_another_file`); it survives a find too. `ClearFind()` in `Show()` resets query, matches and index but leaves wrap alone.

**Docs:** README gained `F3` to the keyboard line plus a **Built-in file viewer** feature bullet stating the 4 MB / 64 MB limits. CHANGELOG gained an `Unreleased` section covering the viewer, the Avalonia 12 upgrade, the Windows artifact shrink, and the SSH.NET advisory fix. `plans/backlog.md` gained the ticked viewer item, and — beyond the plan's ask — an open item to adopt `TestContext.Current.CancellationToken` and drop the `xUnit1051` suppression left behind by the xunit.v3 migration.

## Final Recap

The viewer ships end to end: `F3` on a pane row or a focused search result opens one reusable, modeless `ViewerWindow` that renders **text** (line numbers, encoding label), a **hex dump**, or an **image**, across local, SFTP, SMB, S3 and Azure Blob, with find-in-file, word wrap, a truncation notice and **Open in default app**.

**Shape of the change**
- `src/Duetto.Core/Preview/` — 7 files, zero Avalonia dependency: `PreviewKind`, `PreviewLimits`, `ContentSniffer`, `TextEncodingDetector`, `HexDump`, `PreviewContent`, `PreviewLoader`.
- `src/Duetto/` — `ViewerViewModel`, `PreviewLineViewModel`, `ViewerConverters`, `ViewerWindow.axaml(.cs)`; seams and wiring on `MainViewModel` and `MainWindow`; `MatchHighlight` in both palettes.
- `src/Duetto.Core/Remote/AppPaths.cs` — `ViewerWindowJsonPath`.
- Tests: `ContentSnifferTests`, `HexDumpTests`, `PreviewLoaderTests`, `ViewerTests`, `PreviewKeyTests`, `ViewerFindTests` — **73 new tests**, suite 739 → 812, zero failures and zero skips throughout.

**Decisions a future reader should not have to re-derive**
1. Truncation is proven by reading `budget + 1` bytes, not by trusting `Stat` — remote sizes can lie.
2. BMP detection requires the header size field to match the file size, because `"BM"` alone false-positives on ordinary text.
3. UTF-8 validation uses `isFinalBlock: false` so a codepoint cut by the 8 KiB sniff window does not demote a text file to hex.
4. A failed image decode throws `ArgumentException` (verified, not assumed) and degrades to a capped hex dump rather than an error screen.
5. Preview is deliberately outside the `ActiveOperation` gate; the remote *open-in-default-app* download is deliberately inside it.
6. `ViewerWindow` duplicates `MainWindow`'s placement code on purpose — second occurrence, per the repo's own rule of thumb. Extract a `WindowPlacementBinder` if a third window ever needs it.

**Known gaps, honestly**
- No human has driven the viewer in a real window. Coverage is headless key-press tests plus inspected headless renders.
- Remote preview is proven against `InMemoryFileSystemProvider`, not against a live SFTP/SMB/S3/Azure share. The docker-backed `scripts/smoke.sh` suite passes but does not exercise `F3`.
- Windows and Linux binaries remain untested on their target OSes — a pre-existing backlog item, not introduced here.
- Find matches whole lines, not character ranges; that was the agreed design, but it means a match is highlighted as a full-width row.

## Deployment Plan

1. **Merge order.** `feature/avalonia-12` first (already committed, self-contained), then `feature/file-preview-f3`, which is branched off it. Merging the viewer alone onto `main` would not compile — it depends on Avalonia 12 APIs.
2. **Before merging**, run once more on the merge result: `dotnet build Duetto.slnx --no-incremental`, `dotnet test` (expect 812), `dotnet list package --vulnerable --include-transitive` (expect clean), and `scripts/smoke.sh` (15 integration tests; on a host where port 9000 is taken, remap MinIO with a compose override using `ports: !override`).
3. **Interactive acceptance before tagging** — the gap listed above. On macOS: `F3` on a text file, a PNG, a binary, a folder (nothing should happen), and a file on a live SFTP share; then in the viewer confirm the truncation footer on a >4 MB log, `Ctrl`/`Cmd+F` + `n`/`N`, `W`, and `Esc` twice. Confirm the viewer's size and position are restored on the second open (`viewer-window.json` in the app config dir).
4. **Release.** Set the version in `Directory.Build.props` (currently 1.5.0; this is a feature release, so 1.7.0), replace the CHANGELOG `Unreleased` heading with `## 1.7.0 — <date>`, then `VERSION=1.7.0 scripts/publish-all.sh`. Publish into a **clean** `dist/<rid>/` — publish does not delete stale files and the zip step packages whatever is in the directory.
5. **Expected artifact sizes** (from the `0.0.0-viewer` publish): linux-x64 ~45.6 MB, osx-arm64 ~47.0 MB, osx-x64 ~48.8 MB, win-x64 ~47.5 MB. A win-x64 artifact near 73 MB means the `ExcludeNativeSymbolsFromPublish` target in `src/Duetto/Duetto.csproj` stopped working and native Skia/HarfBuzz PDBs are being packaged again.
6. **Rollback.** The viewer is additive — no file format, config schema or wire protocol changed, and `viewer-window.json` is ignored by older builds. Reverting the merge is sufficient; no migration to undo.
