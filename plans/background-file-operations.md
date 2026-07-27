# Background & Cancellable File Operations

Move every potentially slow file operation off the UI thread and make each one
cancellable mid-run, surfaced through one unified progress strip. Copy/move
(`TransferEngine`) and search (`SearchService`) are already fully backgrounded and
cancellable; this work brings **directory listing**, **delete/trash**, and
**rename** up to the same standard and unifies their progress/cancel UI.

## Decisions (locked with the user 2026-07-27)
- **Scope:** directory listing + delete/trash + rename. (New folder is instant —
  left synchronous. Copy/move + search already done.)
- **Progress/cancel UI:** one **unified progress strip** hosts the active
  operation; the transfer strip is generalized to render either a rich transfer
  (determinate bar + pause) or a simple **indeterminate + Cancel** op.
- **Listing display:** while a folder loads, the pane shows **"Loading…" over an
  empty list** (spinner reveal delayed ~100 ms so instant local loads never flash).
- **Delete detail:** **indeterminate spinner + Cancel** (no per-item counter).
  Cancel stops before the next item; already-trashed items stay trashed.

## Design overview
- New `OperationViewModel` base (or `IOperationStripItem`) with the strip's shared
  surface: `Title`, `IsFinished`, `IsIndeterminate`, `CancelLabel`,
  `CancelOrDismissCommand`, `Dismissed` event. `TransferViewModel` implements it
  (adds determinate bar + `TogglePause`). New `SimpleOperationViewModel` wraps a
  `CancellationTokenSource` + worker `Task` for delete/rename/slow-listing.
- `MainViewModel` gains `ActiveOperation` (single slot) replacing the
  transfer-only `ActiveTransfer` binding at `MainWindow.axaml:142`. A
  `DataTemplate` per op type selects the transfer view vs a simple
  spinner+label+Cancel view.
- **Precedence (single slot):** an explicit mutating op (transfer/delete/rename)
  owns the strip and is not preempted. A slow **listing** surfaces in the strip
  only past the latency threshold **and** only when the slot is free; otherwise it
  is shown by the pane's own "Loading…" overlay and cancelled by navigating away.
- **Test seams** (mirroring the existing `LaunchFile` and `ProcessRunner` seams):
  - `PaneViewModel.Lister : Func<string, IReadOnlyList<FileEntry>>` (default
    `DirectoryLister.List`) — lets tests inject a slow/throwing/cancellable lister.
  - `MainViewModel.TrashFn : Func<string, string?>` (default `TrashService.Trash`)
    — lets tests inject a slow/observable trash.
- **Threading rules:** all `ObservableCollection` mutations (`Rows`, `Results`,
  `Output`, `SkippedItems`) stay on the UI thread via `Dispatcher.UIThread`;
  workers return plain data. Each pane owns a `CancellationTokenSource` for its
  in-flight load, cancelled+replaced when a new load starts (supersession).

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**. Run all C# navigation
through Glider (solution `Duetto.slnx`); commit messages plain imperative, no
attribution trailers.

## Phase 1: Unified operation strip infrastructure
Status: Complete

- [x] Add `IStripOperation` interface (`IDisposable`) exposing `IsFinished` +
  `Dismissed` — the minimal contract the strip slot needs.
- [x] Make `TransferViewModel` implement it (already had `IsFinished`/`Dismissed`/
  `Dispose`); no behavior change to transfers.
- [x] Add `SimpleOperationViewModel(string title, CancellationTokenSource cts)`:
  `IsIndeterminate => true`, `CancelOrDismiss` cancels the CTS then raises
  `Dismissed`; `Finish()` flips `IsFinished` + auto-dismisses after 1 s.
- [x] Host the strip via a type-selecting `ContentControl` at
  `MainWindow.axaml` with `DataTemplate`s for `TransferViewModel`
  (existing `ProgressStrip`) and `SimpleOperationViewModel` (new
  `SimpleOperationStrip` — label + indeterminate `ProgressBar` + Cancel).
- [x] `MainViewModel`: add `ActiveOperation` (typed `IStripOperation?`) as the
  single slot; keep `ActiveTransfer` as a derived `ActiveOperation as
  TransferViewModel` for transfer wiring + existing tests. Guard is now "slot
  busy with an unfinished op".

### Verification Plan
- `dotnet build Duetto.slnx -c Debug` → `0 Error(s)`.
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → all existing tests green
  (transfer strip tests unchanged: `ChromeTests`, `TransferUiTests`).
- New test: constructing a `SimpleOperationViewModel`, invoking its Cancel command
  sets `cts.IsCancellationRequested == true` and raises `Dismissed`.

### Phase Summary
Done. Introduced `IStripOperation` (slot contract: `IsFinished` + `Dismissed`) and
`SimpleOperationViewModel` (indeterminate op wrapping a `CancellationTokenSource`;
`CancelOrDismiss` trips the token then dismisses; `Finish()` auto-hides after 1 s).
`TransferViewModel` now implements the interface with zero behavior change.
`MainViewModel.ActiveOperation` is the single strip slot; `ActiveTransfer` survives
as a derived convenience so `TransferUiTests`/`SearchUiTests` are untouched.

**Deviation from plan:** rather than conditionally re-templating `ProgressStrip`
in place, the strip is hosted by a `ContentControl` with one `DataTemplate` per op
type (`ProgressStrip` for transfers, new `SimpleOperationStrip` for the rest). This
avoids fragile `x:DataType` gymnastics and keeps each view compile-bound to its own
VM — cleaner realization of the same "unified strip" decision.

**Verified:** `dotnet test` → **132 passed** (131 + new `OperationStripTests`), 0
errors. `ChromeTests` render the real `MainWindow` (`new MainWindow(vm); Show()`),
so the new `ContentControl` host loads at runtime. Watched the new test fail first
(`CS0246: SimpleOperationViewModel not found`) before implementing.

**For the next phase:** the slot is single-occupancy; a delete/rename/slow-listing
creates a `SimpleOperationViewModel`, wires `Dismissed` → clear+dispose the slot,
and assigns it to `ActiveOperation`. The `SimpleOperationStrip` DataTemplate only
instantiates when such an op is live (first exercised in Phase 3 / on screen).

## Phase 2: Background directory listing
Status: Complete

- [x] Add `Lister` seam to `PaneViewModel` (default `DirectoryLister.List`).
- [x] Add `[ObservableProperty] bool _isLoading;` and a "Loading…" overlay in
  `PaneView.axaml` shown over the (empty) list while a load is in flight.
- [x] Convert `Reload` to an async pipeline (`StartLoad` → `ApplyWhenReady` →
  `ApplyRows`): capture marks/cursor, cancel+replace the pane's load CTS, run
  `Lister` + `EntrySorter.Sort` via a `LoadScheduler` seam, then rebuild `Rows` +
  restore selection. Stale/superseded results are discarded (token + CTS-identity
  check).
- [x] Update all callers via a `selectAfter`/`selectFirst` thread through
  `StartLoad`: `SetPath`, `NavigateTo(path, selectName)`, `Up`, `CommitRename`,
  `NewFolder`, plus `MainViewModel` reveal/`TryNavigatePath` now use the
  `selectName` overload so selection lands after the async load.
- [x] `Reloaded` fires at the end of `ApplyRows` (after Rows populated).
- [~] Slow-listing-in-strip precedence: deferred to Phase 5 (listing currently
  shows only the pane overlay; strip stays reserved for delete/rename/transfer).

### Verification Plan
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → green, including existing
  `PaneTests`, `PathNavigationTests`, `EdgeCaseTests`.
- New headless tests (inject `Lister`):
  - Navigate → `Rows` eventually populated; `.` parent row present.
  - Inject a lister that blocks on a gate: assert `IsLoading == true` while
    blocked, then rows appear and `IsLoading == false` after release.
  - Rapid navigate A→B while A's lister is still blocked: only B's contents land
    (A's superseded result is discarded); A's CTS is cancelled.
  - Marks/cursor preserved across a `preserveSelection: true` reload.

### Phase Summary
Done. Listing is now async off the UI thread via a **`LoadScheduler` seam** —
default runs inline (`Task.FromResult(work())`) so the ~40 existing synchronous
pane assertions stay valid untouched; production wires
`PaneViewModel.BackgroundScheduler` (`Task.Run`) on the parameterless
`MainViewModel()` ctor (production-only path). Per-pane `_loadCts` cancels the prior
load on every new one; `ApplyWhenReady` bails on `OperationCanceledException` or if
a newer CTS has taken over, so rapid navigation always lands on the final dir.
`IsLoading` drives a "Loading…" overlay in `PaneView.axaml`. Selection-after-load is
threaded through `StartLoad(selectAfter, selectFirst)` and a new
`NavigateTo(path, selectName)` overload (used by Up, rename, new-folder, search
reveal, address-bar file navigation).

**Key decisions:** the scheduler seam (not a raw `Task.Run` in the VM) is what makes
the change testable without rewriting the suite — a `ManualScheduler` in
`BackgroundListingTests` releases loads on command to exercise `IsLoading` and
supersession deterministically. `Reload` now returns `Task` (callers that ignore it
compile unchanged); `LoadCompletion` is the await handle.

**Verified:** `dotnet test` → **134 passed** (132 + 2 new background tests), 0
errors. Watched both new tests fail first (`CS1061: LoadScheduler/IsLoading`
missing). No remaining direct `DirectoryLister.List` callers outside the seam.

**Deferred to Phase 5:** surfacing a *slow* listing in the unified strip (with the
free-slot precedence rule). Today a slow listing shows the pane overlay and is
cancelled by navigating away; wiring it into the strip is the remaining bit of the
"unified strip for every long op" decision.

## Phase 3: Background delete / trash
Status: Not started

- [ ] Add `TrashFn` seam to `MainViewModel` (default `TrashService.Trash`).
- [ ] Rewrite `DeleteSelected`: snapshot target paths on the UI thread, create a
  `SimpleOperationViewModel` ("Deleting N items"), set it as `ActiveOperation`,
  then loop `TrashFn` on `Task.Run` checking the CTS **before each item**. On
  completion, marshal row removal (`Search.Results` / pane `Rows`) + pane reloads
  to the UI thread and `Finish()` the op.
- [ ] Cancel semantics: cancelling stops before the next item; items already
  trashed remain removed and their rows refreshed.
- [ ] Keep the existing exception swallow (`IOException`,
  `UnauthorizedAccessException`, `FileNotFoundException`) per item so one failure
  doesn't abort the batch.

### Verification Plan
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → green.
- New headless tests (inject `TrashFn`):
  - Delete N marked rows → `TrashFn` called N times; rows gone; `ActiveOperation`
    finishes and clears.
  - Inject a `TrashFn` that blocks after the first item, cancel the op → remaining
    items NOT trashed; first item stays trashed.
  - A per-item throw is swallowed; the batch continues.

### Phase Summary
_(write when phase completes)_

## Phase 4: Background rename
Status: Not started

- [ ] Run `FileOps.Rename` off the UI thread in `PaneViewModel.CommitRename` via
  `Task.Run`; on success marshal `ReloadAsync` + `SelectByName` to the UI thread.
- [ ] If the rename exceeds the latency threshold, show a `SimpleOperationViewModel`
  ("Renaming …") in the strip with Cancel. **Document the limitation:** a single
  OS `File.Move`/`Directory.Move` cannot be interrupted mid-op; Cancel abandons the
  wait/refresh, it does not roll back. Same-volume renames are effectively instant;
  the slow case is a cross-volume directory move.
- [ ] Keep the current exception swallow (`IOException`,
  `UnauthorizedAccessException`, `ArgumentException`) and the "no-op on
  empty/unchanged name" guard.

### Verification Plan
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → green, including existing
  rename coverage.
- New headless test: rename a file → new name present in `Rows` after the async
  refresh; cursor lands on the renamed row.

### Phase Summary
_(write when phase completes)_

## Phase 5: Integration, precedence & regression
Status: Not started

- [ ] Verify the single-slot precedence end to end: a running transfer is not
  preempted by a slow listing; delete/rename occupy the slot; listing falls back to
  the pane overlay when the slot is busy.
- [ ] Confirm no `ObservableCollection` is mutated off the UI thread (audit every
  new `Task.Run` body) — Avalonia throws or corrupts otherwise.
- [ ] Confirm `Dispose` paths cancel in-flight loads/ops (pane load CTS, delete
  CTS) so closing mid-operation doesn't leak or throw.
- [ ] Mark the backlog item at `plans/backlog.md:6` (background directory listing)
  `- [x]` and note this plan.
- [ ] Full suite green; manual smoke via `--screenshot` unaffected.

### Verification Plan
- `dotnet build Duetto.slnx -c Debug` → `0 Error(s)`.
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → **all** green (existing
  131 + new).
- `dotnet run --project src/Duetto -- --smoke` → headless render+exit, exit code 0.

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete)_

## Deployment Plan
_(write when all phases complete)_
