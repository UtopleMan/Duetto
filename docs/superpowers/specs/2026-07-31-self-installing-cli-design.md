# Self-installing `duetto` CLI — design

## Goal

After the app is installed, typing `duetto` (optionally with a folder argument,
e.g. `duetto .`) in a shell launches Duetto. Because a drag-to-Applications DMG
cannot run install scripts, the app installs the CLI launcher itself on launch.

## Decisions

- **When:** on every normal (non-headless) launch, best-effort. Headless
  smoke/screenshot/CI runs skip it.
- **Idempotent:** if a `duetto` command already resolves on PATH, do nothing.
- **Where:** the first directory in the user's `PATH` that exists and is
  writable (typically `/opt/homebrew/bin` or `/usr/local/bin`). Using a PATH
  entry guarantees the command is actually reachable.
- **Launcher content:**
  ```sh
  #!/bin/sh
  exec "<app's own executable>" "$@"
  ```
  Targets the running app's own binary via `Environment.ProcessPath`, not a
  hardcoded `/Applications` path, so it works wherever the app lives. `exec`
  keeps it simple; the shell's working directory is inherited so `duetto .`
  resolves `.` against the caller's directory.
- **Best-effort:** any failure (no writable PATH dir, permission denied, IO
  error) is swallowed and never blocks or delays startup.

## Architecture

### `Duetto.Core.Cli.CliInstaller` (Avalonia-free, unit-tested)

Pure decision + injected IO:

```csharp
public sealed class CliInstaller
{
    public CliInstaller(
        Func<string, bool> commandExists,          // does the command already resolve on PATH?
        IReadOnlyList<string> candidateDirs,        // PATH dirs, in order
        Func<string, bool> isWritable,              // can we write into this dir?
        Action<string, string> writeExecutable);    // write script + mark executable

    public static string BuildLauncherScript(string appExecutablePath);

    // Returns the path written, or null when nothing was written
    // (command already present, or no writable directory).
    public string? EnsureInstalled(string commandName, string appExecutablePath);
}
```

`EnsureInstalled`: if `commandExists(name)` → return null. Otherwise pick the
first `candidateDirs` entry where `isWritable` is true, write
`BuildLauncherScript(appExecutablePath)` to `dir/name`, return that path. If none
are writable → null.

### Production glue (Duetto app, thin, not unit-tested)

A helper builds the production `CliInstaller`:
- `commandExists` — scan `PATH` dirs for an executable file named `duetto`.
- `candidateDirs` — `PATH` split on `:`, keeping rooted existing directories in
  order.
- `isWritable` — directory exists and a probe write succeeds.
- `writeExecutable` — `File.WriteAllText` then set the executable bit
  (`UnixFileMode`).

`Program.Main` calls it fire-and-forget (`Task.Run`) when
`!Options.Headless`, wrapped so every exception is swallowed. The app executable
path is `Environment.ProcessPath`.

## Tests

`CliInstallerTests` (plain xUnit, injected IO):
- `BuildLauncherScript` contains the shebang, `exec`, the quoted app path, and `"$@"`.
- already-present command → no write, returns null.
- not present, first dir not writable, second writable → writes to the second,
  returns its path.
- not present, no writable dir → no write, returns null.
- written content equals `BuildLauncherScript(appPath)`.

## Out of scope (YAGNI)

- Windows / Linux launcher variants (this targets the macOS build; the logic is
  portable but only wired for the shipped app).
- Upgrading or repointing an existing stale `duetto` on PATH — present means skip.
- An uninstall path or a first-run prompt — install is silent and idempotent.
- `nohup &` detachment — `exec` is used; can revisit if terminal attachment is a
  problem.
