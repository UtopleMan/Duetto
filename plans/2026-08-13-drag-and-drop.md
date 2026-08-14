# Drag and drop (panes + OS)

Add drag-and-drop to Duetto: move/copy files by dragging **between the left and
right pane** (all backends), **from the OS into a pane** (Finder/Explorer → Duetto,
all backends incl. upload to remote), and **from a pane out to the OS** (Duetto →
Finder/desktop, local files only in this pass). Reuses the existing
`TransferEngine`/`StartTransfer` machinery so backends, conflict-skip, and progress
UI come for free.

## Locked decisions
- **Directions:** internal pane↔pane, OS→Duetto drop-in, Duetto→OS drag-out.
- **Default effect:** **Copy**. Hold **Shift** to **Move**. (Applies to internal
  DnD and OS drop-in.)
- **Drag-out effect:** **copy/export only — source is NEVER deleted**, regardless of
  the OS-reported effect. No Shift=move for drag-out.
- **Drop target granularity:** **whole pane** — any drop on a pane targets that
  pane's current directory. No per-row folder hit-testing/highlight.
- **Backend scope:**
  - Internal pane↔pane and OS→Duetto drop-in work for **all** backends (local,
    SFTP, SMB, S3, Azure) — they route through `StartTransfer`, which already
    resolves source/dest providers via `Registry` and uploads local→remote.
  - OS drag-**out** is **local-filesystem only** in this pass. Remote drag-out
    (download-to-temp staging) is captured as **Phase 4, deferred** per the
    local-first decision.
- **Avalonia constraint (why Phase 4 is deferred):** the OS needs real file bytes
  before the drop; Avalonia has no cross-platform delayed-rendering / file-promise,
  so remote content can't be materialised on-demand mid-drag. It must be staged to
  temp first — deferred.
- **Testability seam:** all decision logic lives in **`MainViewModel` public
  methods** taking plain data (path lists, target pane, a `move` bool). The
  `PaneView` code-behind is a thin adapter that extracts paths/modifier from the
  Avalonia drag events and calls the VM. Headless `[AvaloniaFact]` tests drive the
  VM methods directly (the live OS gesture / `DoDragDropAsync` is not headless-
  testable — only the payload/gating logic is).
- **No new NuGet dependencies.** Avalonia 11.3.18 ships DnD. **Implementation note:**
  confirm the exact API surface against 11.3.18 at build time — new
  `DataTransfer`/`DataFormat.File`/`DragDrop.DoDragDropAsync` (per current docs) vs
  classic `DataObject`/`DataFormats.Files`/`DragDrop.DoDragDrop`. Use whichever
  11.3.18 exposes; the plan's logic is API-shape-agnostic.
- **No commented-out code, no underscore fields, primary constructors, file-scoped
  namespaces** — per AGENTS.md.

## Key integration points (verified)
- `MainViewModel.StartTransfer(IReadOnlyList<string> paths, string destinationDir,
  TransferMode mode, PaneViewModel? sourcePane, string sourceScope)` — private today
  (`src/Duetto/ViewModels/MainViewModel.cs:567`). Already guards empty selection and
  an in-flight operation. New DnD entry points wrap it.
- `MainViewModel.Left` / `.Right` / `.ActivePane` / `.InactivePane` /
  `.Registry` — pane model + provider resolver.
- `PaneViewModel.CurrentPath`, `.SelectedRows`, `.MarkedRows`, `.Rows`, `.Reload(...)`
  (`src/Duetto/ViewModels/PaneViewModel.cs`).
- `src/Duetto/Views/PaneView.axaml` — `ListBox x:Name="RowList"` (Grid.Row=2),
  root `UserControl x:Name="Root"`. Drag source + drop target attach here.
- `src/Duetto/Views/PaneView.axaml.cs` — constructor already tunnels a
  `PointerPressed` handler on `RowList`; extend for drag-threshold + drop handlers.
- Test pattern: `tests/Duetto.Tests/Ui/TransferUiTests.cs` — `new MainViewModel(src,
  dst)`, `vm.Left.SelectByName(...)`, `await vm.ActiveTransfer!.Session.Completion`.
  Remote/in-memory backend: `tests/Duetto.Tests/Support/InMemoryFileSystemProvider.cs`.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**. Branch:
`feature/drag-and-drop`. Run `dotnet build` and `dotnet test` before committing.
Phase 4 is intentionally **deferred** — do not implement it unless explicitly asked.

## Phase 1: Internal pane ↔ pane drag and drop (all backends)
Status: Complete

- [x] Add `MainViewModel.DropBetweenPanes(PaneViewModel source, PaneViewModel target,
      bool moveRequested)`: no-op if `source == target`; resolve
      `mode = moveRequested ? Move : Copy`; call `StartTransfer(source.SelectedRows
      paths, target.CurrentPath, mode, sourcePane: source, sourceScope:
      source.CurrentPath)`. `SelectedRows` already returns marked-else-cursor, so the
      redundant "MarkedRows if any" step was dropped. `StartTransfer` guards empty
      selection and an in-flight op.
- [x] In `PaneView.axaml`: set `DragDrop.AllowDrop="True"` on the pane root.
- [x] In `PaneView.axaml.cs`: detect a drag gesture on `RowList` — pointer-move past a
      4px threshold while the left button is pressed on a real row starts an Avalonia
      drag. **API change:** Avalonia 11.3.18's new `DataTransfer` API cannot carry an
      arbitrary in-process object, so the payload is a **string side-token**
      (`"left"`/`"right"`) in a `DataFormat<string>` application format
      `"duetto.pane-source"` (identifier: ASCII letters/digits/`.`/`-` only — slashes
      are rejected at runtime). The `Drop` handler maps the token back to the owning
      `MainViewModel.Left`/`.Right`.
- [x] `DragOver` handler (`ResolveDropEffect`): if the token is present and its source
      pane is **not** this pane, set `DragEffects = Shift ? Move : Copy`; otherwise
      `None`. Rejects self-drop.
- [x] `Drop` handler: reads the token, resolves source pane from `MainViewModel`,
      calls `DropBetweenPanes(sourcePane, targetPane, isShift)`. `MainViewModel` is
      obtained from `TopLevel.GetTopLevel(this)` → `Window.DataContext` (the existing
      pattern used throughout `PaneView.axaml.cs`).
- [x] Visual feedback: `PaneViewModel.IsDropTarget` bool + an accent `Border` overlay
      over the rows; set true/false in `DragEnter`/`DragOver`/`DragLeave`/`Drop`.
- [x] Guard: `ResolveDropEffect` returns `None` when
      `ActiveOperation is { IsFinished: false }` (busy).

### Verification Plan
- New `tests/Duetto.Tests/Ui/DragDropTests.cs`:
  - `Drop_between_panes_copies_by_default` — select left file, call
    `vm.DropBetweenPanes(vm.Left, vm.Right, moveRequested: false)`, await, assert file
    exists in right dir AND still in left dir.
  - `Drop_between_panes_with_shift_moves` — `moveRequested: true`, dismiss, assert file
    gone from left, present in right (mirror `Move_selected_moves_and_dismiss_reloads_panes`).
  - `Drop_onto_same_pane_is_ignored` — `vm.DropBetweenPanes(vm.Left, vm.Left, false)`
    then assert `vm.ActiveTransfer` is null / no-op.
- Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DragDropTests"`
  — expected: all pass.
- `dotnet build` — expected: no warnings/errors.

### Phase Summary
Done. `DropBetweenPanes` added to `MainViewModel` (after the private `StartTransfer`
overload); reuses `StartTransfer` so all backends, conflict-skip, and progress UI come
for free. Drag source + drop handlers live in `PaneView.axaml.cs`; `AllowDrop` + accent
overlay in `PaneView.axaml`; `IsDropTarget` observable on `PaneViewModel`.

Key decision / deviation from the original plan: the internal payload is a **string
side-token**, not an in-process `PaneViewModel` reference — Avalonia 11.3.18's
`DataTransfer`/`DataFormat` API only supports byte[]/string/file/bitmap values, with no
arbitrary-object format. The token (`"left"`/`"right"`) is resolved back to a pane via
`MainViewModel.Left`/`.Right` in the drop handler. Format identifier must be
`[A-Za-z0-9.-]+` (slashes throw at runtime — this bit us once).

The DnD code-behind (gesture, `DoDragDropAsync`, handlers) is **not** headless-testable;
it is covered by compile + the VM-level tests below. Live gesture verified manually is a
follow-up (see Phase 3 manual smoke).

Verification result (2026-08-14): `dotnet test --filter DragDropTests` → **3/3 pass**
(`Drop_between_panes_copies_by_default`, `Drop_between_panes_with_shift_moves`,
`Drop_onto_same_pane_is_ignored`). Full suite → **734/734 pass**. `dotnet build` → 0
errors (pre-existing warnings only: SSH.NET NU1903, MVVMTK0034, CS4014 — none from this
change).

## Phase 2: OS → Duetto drop-in (all backends, incl. upload to remote)
Status: Complete

- [x] Add `MainViewModel.DropFromOs(PaneViewModel target, IReadOnlyList<string>
      localPaths, bool moveRequested)`: no-op if `localPaths` empty; `mode = Move :
      Copy`; call `StartTransfer(localPaths, target.CurrentPath, mode, sourcePane:
      null, sourceScope: localPaths[0])`. `localPaths` are absolute local OS paths
      (provider-local for the local provider); `sourceScope = localPaths[0]` (a local
      path) so `Registry.Resolve` yields the local provider. A remote
      `target.CurrentPath` reuses the existing local→remote upload path.
- [x] In `PaneView.axaml.cs` `ResolveDropEffect`: if `e.DataTransfer.TryGetFiles()`
      returns files, set `DragEffects = Shift ? Move : Copy`; the internal-token branch
      from Phase 1 is checked first.
- [x] In `OnDrop`: OS-file branch extracts absolute local paths via
      `IStorageItem.TryGetLocalPath()` (helper `OsFilePaths`), calls
      `DropFromOs(targetPane, paths, isShift)`.
- [x] The Phase 1 target-pane highlight applies to OS drags (same `UpdateDropFeedback`).

### Verification Plan
- Extend `DragDropTests.cs`:
  - `Drop_from_os_copies_into_local_pane` — create temp files on disk, call
    `vm.DropFromOs(vm.Right, [file1, file2], false)`, await, assert files exist under
    right pane dir and originals still present.
  - `Drop_from_os_with_shift_moves` — `moveRequested: true`, await + dismiss, assert
    originals deleted, copies present.
  - `Drop_from_os_uploads_into_remote_pane` — build a `MainViewModel` whose right pane
    is backed by `InMemoryFileSystemProvider` (follow `RemoteOpsTests`/`TransferUi`
    remote setup), drop a local file, assert it lands in the in-memory backend.
- Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DragDropTests"`
  — expected: all pass.

### Phase Summary
Done. `DropFromOs` added to `MainViewModel` right after `DropBetweenPanes`; also routes
through `StartTransfer`, so the local→remote upload path (already covered by
`RemoteOpsTests.Cross_provider_transfer_local_to_remote`) is reused unchanged for
dropping OS files onto a remote pane. Code-behind: `TryGetFiles()` gates the OS branch in
`ResolveDropEffect`; `OsFilePaths` maps `IStorageItem` → local path via
`StorageProviderExtensions.TryGetLocalPath` (needs `using Avalonia.Platform.Storage`).

Verification result (2026-08-14): `dotnet test --filter DragDropTests` → **6/6 pass**
(adds `Drop_from_os_copies_into_local_pane`, `Drop_from_os_with_shift_moves`,
`Drop_from_os_uploads_into_remote_pane` — the last drives a `fake://host` in-memory
backend and asserts the file lands in it). Full suite → **737/737 pass**. `dotnet build`
→ 0 errors.

## Phase 3: Duetto → OS drag-out (local files only, copy/export)
Status: Complete

- [x] Add `MainViewModel.LocalDragPayload(PaneViewModel source):
      IReadOnlyList<string>?` — returns absolute **local** paths of the current
      selection for a **local** pane (gated on `!PathUtil.IsRemote(CurrentPath)`);
      returns `null` for any remote pane, and also when the selection is empty.
      Selection = `SelectedRows` (marked-else-cursor), matching Phase 1.
- [x] In `PaneView.axaml.cs` `StartPaneDragAsync` (the Phase 1 threshold gesture): when
      `LocalDragPayload` is non-null, add `DataFormat.File` items to the same
      `DataTransfer` — each built from `StorageProvider.TryGetFileFromPathAsync` /
      `TryGetFolderFromPathAsync` (folders handled too) via `DataTransferItem.CreateFile`.
- [x] Never delete the source after a drag-out: Duetto has no drag-out cleanup path at
      all — the OS-returned effect is not acted on. (`DoDragDropAsync` is called with
      `Copy | Move` so an *internal* drop can still show a move cursor; internal move is
      performed by our own `Drop` handler, not by the drag's return value.)
- [x] One gesture carries both: the internal `duetto.pane-source` token AND (for a local
      pane) the OS `DataFormat.File` items ride the same `DataTransfer`, so a drag from a
      local pane works to the other pane *and* to Finder.

### Verification Plan
- Extend `DragDropTests.cs`:
  - `Local_drag_payload_returns_selection_for_local_pane` — select 2 files, assert
    `vm.LocalDragPayload(vm.Left)` equals their absolute paths.
  - `Local_drag_payload_is_null_for_remote_pane` — in-memory-backed pane → assert
    `vm.LocalDragPayload(remotePane)` is `null`.
  - (The live `DoDragDropAsync` gesture itself is out of headless scope; gating +
    payload are covered above.)
- Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DragDropTests"`
  — expected: all pass.
- Manual smoke (macOS, user's platform): drag a local file from a pane to Finder —
  file copied, source untouched.

### Phase Summary
Done. `LocalDragPayload` added to `MainViewModel` after `DropFromOs`. Code-behind
`StartPaneDragAsync` now appends `DataFormat.File` items (via `DataTransferItem.CreateFile`
+ `StorageProvider.TryGetFileFromPathAsync`/`TryGetFolderFromPathAsync`) to the same
`DataTransfer` that already carries the internal pane token, so one gesture serves both
internal DnD and OS drag-out. No source-deletion path exists for drag-out (export-only by
construction).

Verification result (2026-08-14): `dotnet test --filter DragDropTests` → **8/8 pass**
(adds `Local_drag_payload_returns_selection_for_local_pane`,
`Local_drag_payload_is_null_for_remote_pane`). Full suite → **739/739 pass**. `dotnet
build` → 0 errors. Live `DoDragDropAsync` gesture is out of headless scope — **manual
macOS smoke still pending** (drag a local file to Finder; confirm copy + source
untouched).

## Phase 4: Remote drag-out via temp staging — DEFERRED (follow-up)
Status: Deferred — do not implement without explicit ask

Design captured for a future pass (per the local-first decision):
- On drag-start from a **remote** pane, download the selection to a temp staging dir
  under the app's temp area (reuse `TransferEngine` remote→local; surface progress via
  the existing progress strip), THEN `DoDragDropAsync` with the staged local files
  (copy-only). Because staging must complete before the OS drag can grab real bytes,
  the drag "grabs" only after download — acceptable trade documented for the user.
- Temp cleanup: best-effort after the drag completes, plus a sweep on app exit.
- Guard large/many-file selections (size/count threshold) with a toast pointing to
  F5/F6 copy, if the staging wait is deemed too long.

### Verification Plan
_(define when/if this phase is scheduled)_

### Phase Summary
_(deferred)_

## Final Recap
Drag-and-drop shipped for the three active directions, all routed through the existing
`StartTransfer`/`TransferEngine` so every backend, conflict-skip, and the progress strip
work unchanged:

- **Internal pane ↔ pane** (all backends) — `MainViewModel.DropBetweenPanes`. Copy
  default, Shift = Move, self-drop is a no-op.
- **OS → Duetto** (all backends, incl. upload to remote) — `MainViewModel.DropFromOs`.
  Copy default, Shift = Move; remote target reuses the local→remote upload path.
- **Duetto → OS** (local files only, export-only) — `MainViewModel.LocalDragPayload`
  gates the OS file payload; remote panes opt out.

All decision logic lives in three public `MainViewModel` methods taking plain data, driven
directly by 8 headless `[AvaloniaFact]` tests in `tests/Duetto.Tests/Ui/DragDropTests.cs`.
`PaneView.axaml.cs` is a thin adapter: a 4px drag-threshold gesture on `RowList` and the
`DragEnter/Over/Leave/Drop` handlers extract paths + modifiers and call the VM.

Files touched:
- `src/Duetto/ViewModels/MainViewModel.cs` — `DropBetweenPanes`, `DropFromOs`,
  `LocalDragPayload`.
- `src/Duetto/ViewModels/PaneViewModel.cs` — `IsDropTarget` observable.
- `src/Duetto/Views/PaneView.axaml` — `DragDrop.AllowDrop="True"` + accent drop overlay.
- `src/Duetto/Views/PaneView.axaml.cs` — drag gesture, `DoDragDropAsync`, DnD handlers.
- `tests/Duetto.Tests/Ui/DragDropTests.cs` — 8 new tests.

Key implementation notes for the next agent:
- Avalonia 11.3.18's new `DataTransfer`/`DataFormat` API carries only byte[]/string/file/
  bitmap — **no arbitrary in-process object**. The internal drag carries a `"left"`/
  `"right"` string token (`DataFormat<string>`, app-format id `duetto.pane-source`),
  resolved back to `MainViewModel.Left`/`.Right` at drop. Application-format identifiers
  accept only `[A-Za-z0-9.-]` — a slash throws in the static ctor at first construction.
- The classic `DataObject`/`DataFormats.Files`/`DoDragDrop` API is present but obsolete;
  we use the new surface throughout.
- **Phase 4 (remote drag-out via temp staging) remains deferred** — do not implement
  without an explicit ask.

Status: all active phases (1–3) **Complete**; full suite **739/739 pass**, `dotnet build`
0 errors. One manual check outstanding: live macOS drag-out to Finder (Phase 3 smoke).

## Deployment Plan
This is a client desktop app; "deployment" = merge + release build. No migrations, config,
or infra changes.

1. **Manual smoke on macOS** (the one thing headless tests can't cover), on
   `feature/drag-and-drop`:
   - `dotnet run --project src/Duetto` (adjust if the runnable project differs).
   - Internal: drag a file left→right = copy (source stays); Shift-drag = move.
   - OS→Duetto: drag a file from Finder onto a pane = copy into that pane's dir;
     Shift = move. Repeat onto a connected remote pane = upload.
   - Duetto→OS: drag a local file from a pane to Finder = copy, source untouched.
     Confirm a drag from a **remote** pane exposes no OS file (drag-out disabled).
2. **Pre-merge gate:** `dotnet build` (0 errors) and `dotnet test` (739/739) — both green.
3. **Merge:** open a PR from `feature/drag-and-drop` → `main` (commit style `feat:`);
   squash-merge after review.
4. **Release:** follow the existing release-commit convention (see
   `chore(release): vX.Y.Z` history) — bump version, tag, build artifacts. DnD needs no
   new dependency and no runtime flag, so nothing else to toggle.
