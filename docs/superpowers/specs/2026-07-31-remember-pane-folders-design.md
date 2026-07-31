# Remember pane folders — design

## Goal

On launch with no command-line folder argument, both panes reopen at the folders
they were showing when the app last closed. With a folder argument, the argument
opens in the left pane and the right pane still restores its remembered folder.

## Decisions

- **Persist:** the left and right pane directories, saved on close.
- **No-arg launch:** left and right restore their saved folders.
- **Arg launch:** left = argument; right = saved folder.
- **Unusable saved path** (missing directory, or a remote `sftp://…` address):
  falls back to home. Validation is `Directory.Exists`, so remote and deleted
  paths both fall back.

## Architecture

Mirrors the existing window-placement persistence.

### `Duetto.Core.State.SessionState`

```csharp
public sealed record SessionState(string LeftPath, string RightPath);
```

### `Duetto.Core.State.SessionStore`

Same shape as `WindowPlacementStore`:

- `SessionStore(string path)` — real filesystem, atomic temp-then-move write.
- `SessionStore(string path, Func<string,string?> reader, Action<string,string> writer)`
  — injected IO for tests.
- `SessionState? Load()` — null on missing / empty / corrupt; never throws.
- `void Save(SessionState state)` — `System.Text.Json`, indented.

Backed by **`AppPaths.SessionJsonPath`** → `session.json` in `ConfigDir`.

### Path resolution — `MainViewModel.ResolveStartupPaths` (pure, static, testable)

```csharp
public static (string Left, string Right) ResolveStartupPaths(
    string? folderArg, SessionState? saved, string home)
{
    string left  = folderArg ?? (Usable(saved?.LeftPath)  ? saved!.LeftPath  : home);
    string right =              Usable(saved?.RightPath) ? saved!.RightPath : home;
    return (left, right);
}
static bool Usable(string? p) => p is not null && Directory.Exists(p);
```

`folderArg` is `AppOptions.Folder`, already a validated absolute directory, so it
needs no re-check.

### Wiring (`MainViewModel`)

- The big constructor gains `SessionStore? sessionStore = null`, retained in a
  field. Test constructors leave it null → no disk writes.
- The production parameterless constructor loads `session.json` **once**, resolves
  both pane start paths, and retains the store — routed through a private
  tuple-taking constructor so the file is read a single time. The store is null
  when `Program.Options.Headless` (smoke / screenshot / CI never touch
  `session.json`).
- New `public void SaveSession()` →
  `_sessionStore?.Save(new SessionState(Left.CurrentPath, Right.CurrentPath))`.

### Wiring (`MainWindow`)

`OnClosing` calls `Vm.SaveSession()` (no-op when no store), alongside the
existing window-placement save.

## Tests

- **SessionStore:** missing → null, round-trip, corrupt → null, empty → null,
  reader throws `IOException` → null.
- **`ResolveStartupPaths`:** arg + valid saved-right → (arg, saved-right); arg +
  missing saved-right → (arg, home); no arg + both valid → both restored; no arg
  + null session → both home; no arg + missing saved-left → left home; remote-ish
  (nonexistent) saved path → home.
- **`AppPaths.SessionJsonPath`** → `session.json` under `ConfigDir`.
- **`SaveSession`** round-trip: VM built with an injected store, navigate panes,
  `SaveSession()`, assert the store holds both current paths.
- **Headless close → save:** a `MainWindow(vm)` whose vm has an injected session
  store; open, navigate the left pane, close → the store received the session.

## Out of scope (YAGNI)

- Remembering remote connections or auto-reconnecting a saved `sftp://…` pane.
- Per-pane scroll position, selection, or sort.
- Any pane state beyond the two directories.
