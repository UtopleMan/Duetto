# Remember window placement — design

## Goal

Duetto remembers its window geometry across restarts: on next launch it opens at
the same position and size, and maximized if it was maximized when last closed.

Currently `MainWindow.axaml` hardcodes `Width="1180" Height="700"` with no
position or state persistence.

## Decisions

- **Persist:** position (X/Y), size (Width/Height), and maximized state.
- **Off-screen restore:** if the saved position no longer lands on a connected
  screen (e.g. a monitor was unplugged), ignore the saved placement and open at
  the default size, centered.

## Architecture

Persistence and geometry logic live in `Duetto.Core` (which has no Avalonia
reference), mirroring the existing `ConnectionStore`. The window keeps only thin
glue.

### `Duetto.Core.State.WindowPlacement` (Avalonia-free)

```csharp
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized)
{
    public bool IsVisibleOn(IReadOnlyList<ScreenBounds> screens) =>
        screens.Any(s => X >= s.X && Y >= s.Y && X < s.X + s.Width && Y < s.Y + s.Height);
}

public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);
```

Visibility = the saved top-left corner sits inside some connected screen. If a
monitor is unplugged the corner no longer lands on any screen and the caller
falls back to the default. Point-based, so no DPI/scaling math is needed in Core
(`Position` and `Screen.Bounds` are both physical pixels).

### `Duetto.Core.State.WindowPlacementStore`

Mirrors `ConnectionStore`:

- `WindowPlacementStore(string path)` — real filesystem: reader returns file
  content or null; writer does atomic temp-then-move.
- `WindowPlacementStore(string path, Func<string, string?> reader, Action<string, string> writer)`
  — injected IO for unit tests.
- `WindowPlacement? Load()` — returns null when the file is missing, empty, or
  corrupt; never throws (catches `JsonException`, `IOException`).
- `void Save(WindowPlacement placement)` — serializes with `System.Text.Json`
  (`WriteIndented = true`).

### `AppPaths.WindowJsonPath`

New: `Path.Combine(ConfigDir, "window.json")`, alongside `connections.json` and
`hostkeys.json`.

### MainWindow glue

- The parameterless production ctor (`new MainWindow()`, used only by
  `App.axaml.cs`) wires a `WindowPlacementStore(AppPaths.WindowJsonPath)` and a
  screens provider `() => Screens.All.Select(s => new ScreenBounds(...))`. This
  wiring is **skipped when `Program.Options.Headless`** so smoke / screenshot /
  CI runs never read or write `window.json`.
- The `MainWindow(MainViewModel vm)` ctor stays inert (no store, no provider), so
  the existing UI tests that use it get no disk side effects.
- `OnOpened`: `Load()`. If a placement exists **and** `IsVisibleOn(screens)`,
  apply `Position`, `Width`, `Height`, then set `WindowState = Maximized` when
  flagged. Otherwise leave the XAML default. Seed the normal-bounds trackers.
- Normal-bounds tracking: `PositionChanged` and a size-change handler record the
  current position/size only while `WindowState == Normal`. This means closing
  while maximized still persists a sane restore-to-normal size.
- `OnClosing`: build a `WindowPlacement` from the tracked normal bounds and
  `Maximized = WindowState == Maximized`, then `Save()`. No-op when the store was
  not wired.
- `MainWindow.axaml` gains `WindowStartupLocation="CenterScreen"` so a first run
  with no saved placement opens centered.

### Test seam

`internal MainWindow(MainViewModel vm, WindowPlacementStore store, Func<IReadOnlyList<ScreenBounds>> screens)`
plus `[assembly: InternalsVisibleTo("Duetto.Tests")]`, letting headless tests
inject an in-memory store and a fake screen list.

## Tests

**Store** (plain xUnit, injected IO):
- missing file → null
- round-trip: `Save` then `Load` returns an equal placement
- corrupt JSON → null
- empty content → null
- reader throws `IOException` → null (never throws)

**`IsVisibleOn`** (plain xUnit):
- corner inside the single screen → true
- corner outside all screens → false
- corner on a second monitor → true
- empty screen list → false

**Headless UI** (`AvaloniaFact`, best-effort):
- preload an in-memory store with a placement inside a fake screen → open →
  `Width` / `Height` applied
- open then close with an in-memory store → the store received a placement

## Out of scope (YAGNI)

- Minimized persistence — a window closed while minimized restores as Normal.
- Continuous or debounced saving — placement is saved only on close.
- Multi-window persistence — Duetto has a single main window.
