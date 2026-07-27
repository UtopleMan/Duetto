# New folder / new file inline placeholder — design

## Goal

Creating a new folder **or** a new file should drop an **editable control** into
the list so the user names it in place, instead of writing a default-named entry
to disk and leaving the user to F2-rename it. New file is a new feature; the same
placeholder flow serves both.

## Triggers

- **F7** — New folder (existing key).
- **Shift+F7** — New file (new).
- Toolbar: the current single "New" button becomes a **"New ▾" split menu** with
  *New folder* and *New file* entries (`NewFolderCommand` / `NewFileCommand`).

## Behavior (edit-then-create)

Identical for folder and file; they differ only in the created entry kind and the
row's dot color.

- The trigger inserts an editable **placeholder** row at the top of the list (after
  the ".." row), in edit mode, with a suggested name ("New folder" / "New file",
  uniquified to "New folder 2" etc.) preselected. **Nothing is written to disk yet.**
- Enter / commit: create the entry with the typed name, reload so it sorts into
  place, and select it.
- Escape, or an empty name on commit: discard the placeholder — nothing is created.
- Name collision (typed name already exists) or invalid name (path separators):
  - On **Enter**, keep the edit box open and show a status message
    (`"X" already exists`); the user fixes the name.
  - On **click-away (LostFocus)** with a collision, discard the placeholder — no
    focus trap.

This reuses the existing inline-edit UI (`IsEditing` TextBox, focus-on-attach,
Enter/Esc/LostFocus handlers).

## Why placeholder (not create-then-rename)

Creating the entry first would trip the pane's `FileSystemWatcher`: the creation
fires a 300 ms debounced `Reload` that rebuilds `Rows` with fresh row objects,
wiping `IsEditing` ~300 ms after the box appears. Editing before the disk write
sidesteps the self-clobber (our own action causes no fs change until commit).

## Components

### Core — `FileOps` (`src/Duetto.Core/Operations/FileOps.cs`)

- `SuggestEntryName(parentDir, baseName)` → the existing uniquify loop (already
  checks both `Directory.Exists` and `File.Exists`, so it serves files and folders),
  returning the first free name **without** creating anything.
- `CreateFolder(parentDir, name)` → validate (`name` non-empty, no
  `DirectorySeparatorChar` / `AltDirectorySeparatorChar`), throw if the target
  exists, `Directory.CreateDirectory`, return the full path.
- `CreateFile(parentDir, name)` → same validation + throw-if-exists, create an empty
  file (`File.Create(path).Dispose()`), return the full path.
- `NewFolder(parentDir, baseName = "New folder")` stays, redefined as
  `CreateFolder(parentDir, SuggestEntryName(parentDir, baseName))`, preserving its
  current behavior and existing `FileOpsTests`.

### `FileRowViewModel` (`src/Duetto/ViewModels/FileRowViewModel.cs`)

- Add `bool IsNewPlaceholder { get; }`.
- Add factory `NewPlaceholder(parentPath, suggestedName, isDirectory)` — a synthetic
  entry (mirrors `ParentNav`) whose `IsDirectory` flag drives the dot color (gold
  folder vs grey file) and name weight. Caller sets `EditName` + `IsEditing`.

### `PaneViewModel` (`src/Duetto/ViewModels/PaneViewModel.cs`)

- `NewFolder()` / `NewFile()`: `suggested = FileOps.SuggestEntryName(CurrentPath,
  "New folder" | "New file")`; build the placeholder row (`isDirectory` true/false),
  insert it after the ".." row (index 0 when there is none), set `IsEditing = true`,
  select it. No `StartLoad`, no disk write. Track the active placeholder in a field
  (for watcher survival, below). Both are `[RelayCommand]` → `NewFolderCommand` /
  `NewFileCommand`.
- `CommitRename(row)` — branch on `row.IsNewPlaceholder`:
  - empty name → discard (remove the row; create nothing).
  - valid, no collision → `row.IsDirectory ? FileOps.CreateFolder : FileOps.CreateFile`
    on `(CurrentPath, name)`, clear the tracked placeholder, then
    `StartLoad(preserveSelection: false, selectAfter: name, selectFirst: false)`.
  - collision / invalid → keep editing (`IsEditing` stays true), set `StatusText`
    to the message; do **not** create.
  - non-placeholder rows: existing rename path, unchanged.
- `CancelRename(row)`: placeholder → remove the synthetic row + clear the tracked
  placeholder (nothing created); else existing behavior (`IsEditing = false`).
- **Watcher survival:** `ApplyRows` re-inserts the active editing placeholder (from
  the tracked field) after rebuilding `Rows`, so an unrelated fs change during the
  edit does not drop the synthetic row.

### View — `MainWindow` (`src/Duetto/Views/MainWindow.axaml{,.cs}`)

- `.axaml`: replace the single "New" button (line ~75) with a "New ▾" button whose
  `Flyout` is a `MenuFlyout` of *New folder* → `ActivePane.NewFolderCommand` and
  *New file* → `ActivePane.NewFileCommand`.
- `.axaml.cs` `OnPreviewKeyDown`: add `case Key.F7 when e.KeyModifiers ==
  KeyModifiers.Shift → pane.NewFile()` **before** the plain `case Key.F7`
  (`pane.NewFolder()`), since the switch matches top-down.

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

- `FileOpsTests`: `SuggestEntryName` returns a free name and creates nothing;
  `CreateFolder` / `CreateFile` create the exact name, throw on existing / on
  separators.
- `PaneTests` — update `NewFolder_creates_and_selects` → placeholder is present,
  `IsEditing`, selected, and nothing is on disk yet.
- New tests (folder **and** file variants):
  - commit with a typed name creates that exact folder / file and selects it;
  - commit with an empty name discards the placeholder, creates nothing;
  - `CancelRename` discards the placeholder, creates nothing;
  - collision on Enter keeps `IsEditing` true and creates nothing;
  - `NewFile` placeholder renders as a file (`IsDirectory == false`).

## Out of scope

- Backgrounding entry creation (a single `mkdir` / empty-file create is instant —
  the existing synchronous behavior is kept).
- New-file templates / default extension (the base name is plain "New file"; the
  user types the extension).
- Changing F2 rename of existing entries.
