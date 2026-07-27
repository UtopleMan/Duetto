# New-folder inline placeholder — design

## Goal

F7 / "New folder" should drop an **editable folder control** into the list so the
user names the folder in place, instead of silently creating "New folder" on disk
and leaving the user to F2-rename it afterward.

## Behavior (edit-then-create)

- F7 inserts an editable **placeholder** row at the top of the list (after the
  ".." row), in edit mode, with a suggested name ("New folder", "New folder 2", …)
  preselected. **No directory is written to disk yet.**
- Enter / commit: create the folder with the typed name, reload so it sorts into
  place, and select it.
- Escape, or an empty name on commit: discard the placeholder — nothing is created.
- Name collision (typed name already exists) or invalid name (path separators):
  - On **Enter**, keep the edit box open and show a status message
    (`"X" already exists`); the user fixes the name.
  - On **click-away (LostFocus)** with a collision, discard the placeholder — no
    focus trap.

This mirrors the placeholder model of file managers where the folder is only
committed to disk once named, and reuses the existing inline-edit UI
(`IsEditing` TextBox, focus-on-attach, Enter/Esc/LostFocus handlers).

## Why placeholder (not create-then-rename)

Creating the folder first would trip the pane's `FileSystemWatcher`: the folder's
creation fires a 300 ms debounced `Reload` that rebuilds `Rows` with fresh row
objects, wiping `IsEditing` ~300 ms after the box appears. Editing before the disk
write sidesteps the self-clobber (our own action causes no fs change until commit).

## Components

### Core — `FileOps` (`src/Duetto.Core/Operations/FileOps.cs`)

Split the current one-shot `NewFolder` into reusable pieces:

- `SuggestFolderName(parentDir, baseName = "New folder")` → the existing uniquify
  loop, returning the first free name **without** creating it.
- `CreateFolder(parentDir, name)` → validate (`name` non-empty, no
  `DirectorySeparatorChar` / `AltDirectorySeparatorChar`), throw if the target
  already exists, `Directory.CreateDirectory`, return the full path.
- `NewFolder(parentDir, baseName)` stays, redefined as
  `CreateFolder(parentDir, SuggestFolderName(parentDir, baseName))`, preserving its
  current behavior and existing `FileOpsTests`.

### `FileRowViewModel` (`src/Duetto/ViewModels/FileRowViewModel.cs`)

- Add `bool IsNewPlaceholder { get; }`.
- Add factory `NewPlaceholder(parentPath, suggestedName)` — a synthetic
  directory entry (mirrors `ParentNav`): `IsDirectory = true` so it renders with
  the gold folder dot and medium weight. Caller sets `EditName` + `IsEditing`.

### `PaneViewModel` (`src/Duetto/ViewModels/PaneViewModel.cs`)

- `NewFolder()`: `suggested = FileOps.SuggestFolderName(CurrentPath)`; build the
  placeholder row, insert it after the ".." row (index 0 when there is none), set
  `IsEditing = true`, and select it. No `StartLoad`, no disk write. Track the
  active placeholder in a field (for watcher survival, below).
- `CommitRename(row)` — branch on `row.IsNewPlaceholder`:
  - empty name → discard (remove the row; create nothing).
  - valid, no collision → `FileOps.CreateFolder(CurrentPath, name)`, clear the
    tracked placeholder, then `StartLoad(preserveSelection: false,
    selectAfter: name, selectFirst: false)`.
  - collision / invalid → keep editing (`IsEditing` stays true), set
    `StatusText` to the message; do **not** create.
  - non-placeholder rows: existing rename path, unchanged.
- `CancelRename(row)`: placeholder → remove the synthetic row + clear the tracked
  placeholder (nothing created); else existing behavior (`IsEditing = false`).
- **Watcher survival:** `ApplyRows` re-inserts the active editing placeholder (from
  the tracked field) after rebuilding `Rows`, so an unrelated fs change during the
  edit does not drop the synthetic row.

### View — `PaneView` (`src/Duetto/Views/PaneView.axaml{,.cs}`)

No change. `OnEditBoxAttached` (focus + select-all), `OnEditBoxKeyDown`
(Enter → `CommitRename`, Esc → `CancelRename`), and `OnEditBoxLostFocus`
(→ `CommitRename`) already dispatch generically by row, so the placeholder's
TextBox behaves like a rename box for free.

### LostFocus vs collision

`OnEditBoxLostFocus` calls `CommitRename`. For a placeholder with a colliding name,
`CommitRename` must distinguish "blur" from "Enter" to discard rather than stay
editing (staying editing on blur is a focus trap). Simplest: `CommitRename` treats
collision as *stay editing*; add a small `CommitPlaceholderOnBlur(row)` (or a
`fromBlur` flag) that the LostFocus handler calls, which discards on collision
instead of re-opening the box.

## Testing

- `FileOpsTests`: `SuggestFolderName` returns a free name and does not create it;
  `CreateFolder` creates the exact name, throws on existing / on separators.
- `PaneTests` — update `NewFolder_creates_and_selects` → placeholder is present,
  `IsEditing`, selected, and the directory is **not** on disk yet.
- New `PaneTests` / a `NewFolderTests`:
  - commit with a typed name creates that exact directory and selects it;
  - commit with an empty name discards the placeholder, creates nothing;
  - `CancelRename` discards the placeholder, creates nothing;
  - collision on Enter keeps `IsEditing` true and creates nothing.

## Out of scope

- Backgrounding folder creation (a single `mkdir` is instant — the existing
  synchronous behavior is kept).
- Changing F2 rename of existing files.
