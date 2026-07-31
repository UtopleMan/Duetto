# Native macOS trash — design

## Goal

On macOS, deleting a file or folder should route it through the system trash the
same way Finder does — so items get "Put Back" support and items on other
volumes go to that volume's `.Trashes` instead of failing. Windows and Linux
already trash correctly and are unchanged.

## Problem

`TrashService.TrashMac` moves the item into `~/.Trash` with a raw
`Directory.Move` / `File.Move`. Two defects:

- No "Put Back" metadata — the item is in Trash but Finder can't restore it to
  its original location.
- Cross-volume failure — an item on another volume (external drive, mounted
  image) cannot be moved into `~/.Trash`; `Directory.Move` across volumes throws.

## Design

### `Duetto.Core.Operations.MacTrash` (macOS-only, Objective-C interop)

`static string Trash(string fullPath)`:

- Uses `libobjc` (`objc_getClass`, `sel_registerName`, `objc_msgSend`) to call
  `[[NSFileManager defaultManager] trashItemAtURL:url resultingItemURL:&out error:&err]`.
- Builds the `NSURL` via `NSString stringWithUTF8String:` → `NSURL fileURLWithPath:`.
- On success, reads the resulting item's `path` and returns it (POSIX path in the
  trash).
- On failure (non-zero return), throws `IOException` carrying the `NSError`
  `localizedDescription`.

This is the API Finder uses, so it records Put Back metadata and handles other
volumes correctly.

### `TrashService.TrashMac`

```
try { return MacTrash.Trash(fullPath); }
catch (interop/IO failure) { return <existing ~/.Trash move fallback>; }
```

The fallback preserves today's same-volume behavior if the native call ever
fails; delete never silently becomes permanent.

### Unchanged

- `TrashService.Trash` still guards a missing source with `FileNotFoundException`.
- Windows (`SHFileOperationW` → Recycle Bin) and Linux (FreeDesktop
  `Trash/files` + `.trashinfo`) paths are untouched.
- Return contract: trash destination path on Unix, `null` on Windows.

## Tests

- **Existing, stay green:** `Trash_removes_source` (source gone, non-Windows
  returns an existing trash path) and `Trash_missing_file_throws`. Native trash
  satisfies both.
- **New macOS cross-volume test** — the reported deficiency and a real RED→GREEN:
  1. `hdiutil create` a small temp DMG, `hdiutil attach` it.
  2. Create a folder containing a file on the mounted volume.
  3. `TrashService.Trash(folder)` → assert the source folder is gone.
  4. Detach the image and delete the DMG; remove the trashed item.
  - With the current naive move, step 3 throws (`Directory.Move` across volumes)
    → RED. With native trash it succeeds → GREEN.
  - macOS-only; if `hdiutil` or the mount is unavailable (e.g. CI), the test
    skips cleanly. Runs on the macOS dev machine.

## Out of scope (YAGNI)

- Any change to Windows or Linux trash.
- Remote (SFTP) trash — remote deletes remain permanent (no `HasTrash`).
- A "delete permanently" shortcut.
- Autorelease-pool management around the few short-lived interop objects.
