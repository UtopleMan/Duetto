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
Status: Not started

- [ ] Add `OperationViewModel` base (or `IOperationStripItem` interface) in
  `src/Duetto/ViewModels/` exposing `Title`, `IsFinished`, `IsIndeterminate`,
  `CancelLabel`, `CancelOrDismissCommand`, and the `Dismissed` event.
- [ ] Make `TransferViewModel` implement it (`IsIndeterminate => false`); no
  behavior change to transfers.
- [ ] Add `SimpleOperationViewModel(string title, CancellationTokenSource cts)`:
  `IsIndeterminate => true`, `CancelOrDismiss` cancels the CTS then raises
  `Dismissed`; exposes a `Task Completion` hook and a `Finish()` that flips
  `IsFinished` + auto-dismisses (reuse the transfer strip's 1.5 s auto-hide).
- [ ] Generalize `ProgressStrip.axaml`: keep the rich transfer layout for
  `IsIndeterminate == false`; add an indeterminate layout (label + spinner +
  Cancel) shown when `IsIndeterminate == true`. Switch via `DataTemplate` /
  `IsVisible` bindings. Keep `x:DataType` compile-safe.
- [ ] `MainViewModel`: add `ActiveOperation` (typed as the base) and repoint
  `MainWindow.axaml:142` `DataContext="{Binding ActiveOperation}"`. Keep the
  existing "one transfer at a time" guard; add a helper `SetActiveOperation(op)`
  that wires `Dismissed` → clear slot + dispose.

### Verification Plan
- `dotnet build Duetto.slnx -c Debug` → `0 Error(s)`.
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → all existing tests green
  (transfer strip tests unchanged: `ChromeTests`, `TransferUiTests`).
- New test: constructing a `SimpleOperationViewModel`, invoking its Cancel command
  sets `cts.IsCancellationRequested == true` and raises `Dismissed`.

### Phase Summary
_(write when phase completes)_

## Phase 2: Background directory listing
Status: Not started

- [ ] Add `Lister` seam to `PaneViewModel` (default `DirectoryLister.List`).
- [ ] Add `[ObservableProperty] bool _isLoading;` and bind a "Loading…" overlay in
  `PaneView.axaml` shown over an empty list; reveal the spinner via a ~100 ms
  `DispatcherTimer` so instant loads don't flash it.
- [ ] Convert `Reload(preserveSelection)` to `ReloadAsync`: capture marks/cursor,
  cancel+replace the pane's in-flight load CTS, run `Lister(CurrentPath)` +
  `EntrySorter.Sort` on `Task.Run(..., token)`, then marshal the `Rows` rebuild +
  selection restore to the UI thread. Ignore results from a superseded token.
- [ ] Update all callers: `SetPath`, `SortBy`, `NavigateTo/Back/Forward/Up`,
  `CommitRename`, `NewFolder`, `OnDebounceTick` (watcher), and
  `MainViewModel`/`CommandBar.CommandFinished` reload calls. Keep synchronous
  public entry points working (fire-and-forget the task where a caller can't await,
  but ensure supersession prevents races).
- [ ] Preserve `Reloaded` event semantics (view restores focus) — fire after Rows
  are populated on the UI thread.
- [ ] Surface a slow listing in the unified strip only past the threshold and only
  when `ActiveOperation` is free (per precedence rule).

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
_(write when phase completes)_

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
