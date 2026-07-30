# CLI folder argument — design

## Goal

Launch Duetto from the command line with a folder path so the left (active)
pane opens there instead of home:

```
duetto ~/Projects
duetto ./relative/dir
duetto --chrome win /tmp
```

## Decisions

- **Arg form:** positional. The first non-flag token is the folder, like
  `open`, `code`, `nautilus`.
- **Scope:** the left / active pane only. The right pane stays at home.
- **Bad path:** silent fallback to home. Missing dir, a file, or garbage input
  is ignored — no crash, no stderr, exit 0.

## Changes

### `AppOptions`

Add:

```csharp
public string? Folder { get; init; }
```

In `Parse`, add a `default:` case to the arg switch:

- Existing flag cases (`--chrome`, `--screenshot`) already consume their value
  via `++i`, so `default` only ever sees genuine positionals.
- Ignore any token that starts with `--` (unknown flag, never a path).
- Take the **first** positional only; ignore extras.
- Resolve the candidate to an absolute path with `Path.GetFullPath` (relative
  paths resolve against the process CWD at launch). Wrap in try/catch —
  `GetFullPath` throws on garbage (`ArgumentException`, `PathTooLongException`,
  `NotSupportedException`) → treat as no folder.
- `Directory.Exists(absolute)` → store the absolute path; otherwise leave
  `Folder` null (the home fallback).

`Folder` holds a validated absolute directory path or null. All parsing and
validation live here — the single testable seam.

### `MainViewModel()` (production ctor)

The left pane path becomes `Program.Options.Folder ?? home`; the right pane
stays home. One-line change. The existing explicit test ctor already takes
`leftPath` / `rightPath`, so no VM-level test wiring is needed for the mapping.

## Tests

New `AppOptionsTests` — plain xUnit `[Fact]`, no Avalonia headless needed:

- valid dir → `Folder` equals the absolute path
- relative dir → resolved against CWD
- missing path → null
- a file path (not a dir) → null
- garbage / invalid path chars → null
- `--chrome win <dir>` → `Chrome == Win` **and** `Folder` set (positional does
  not swallow the chrome value)
- no positional arg → null
- extra positionals → only the first is used

## Out of scope (YAGNI)

- A second folder for the right pane.
- Remote `sftp://…` addresses.
- `~` expansion (the shell already does it).
- An error-exit path for bad input.
