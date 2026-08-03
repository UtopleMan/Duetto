# Open Remote File — download, show, delete on exit

**Date:** 2026-08-03
**Status:** Approved, ready for implementation planning

## Problem

Pressing Enter (or double-clicking) a file row activates it. For local files this
launches the file with the OS default application. For files on a remote pane (SFTP,
SMB, S3) it currently does nothing — `PaneViewModel.Open` explicitly no-ops the
remote-file branch (`src/Duetto/ViewModels/PaneViewModel.cs:226`).

Users expect Enter to work on remote files too. Since an external application cannot
read directly from a remote provider, the file must first be materialised locally.

## Goal

Enter / double-click on a **remote file row** downloads the file to a temporary folder,
opens it with the OS default application, and deletes the temporary copy when the app
exits.

Directory rows and local files are unchanged. Only the remote-file branch of
`PaneViewModel.Open` changes behaviour.

## Scope

**In scope**
- Remote-file open: download to temp, launch, track for cleanup.
- Cleanup of downloaded temp files on clean exit.
- Startup sweep of the temp-open folder to recover files leaked by a previous crashed
  session.
- Progress/cancel affordance during the download.

**Out of scope (confirmed with user)**
- **No edit-back / upload.** The temp copy is view-only. If the user edits and saves it
  in the external app, those changes are NOT synced back to the remote and are lost when
  the temp copy is deleted.
- **No dedup.** Opening the same remote file twice downloads it twice, into two separate
  temp subdirectories.
- No preview pane, no in-app viewer. The OS default application shows the file.

## Current behaviour (baseline)

- Enter: `MainWindow` key handler → `pane.OpenCursor()` → `PaneViewModel.Open(row)`
  (`src/Duetto/Views/MainWindow.axaml.cs:317`).
- Double-click: `PaneView.OnRowDoubleTapped` → `vm.Open(row)`
  (`src/Duetto/Views/PaneView.axaml.cs:109`).
- Both funnel through `PaneViewModel.Open(FileRowViewModel)`
  (`src/Duetto/ViewModels/PaneViewModel.cs:218`):
  - parent-nav row → `Up()`
  - directory row → `NavigateTo(...)`
  - local file → `LaunchFile(fullPath)`
  - **remote file → no-op** (the line this feature replaces)
- `LaunchFile` is an injected `Action<string>` that shell-opens a local path
  (`PaneViewModel.cs:90`); tests replace it to observe calls.
- `FileSystemRegistry.Resolve(address)` returns `(IFileSystemProvider provider, string
  localPath)`; `provider.OpenRead(localPath)` returns a readable `Stream`
  (`src/Duetto.Core/FileSystem/IFileSystemProvider.cs:35`).
- `SimpleOperationViewModel` is a ready-made indeterminate progress strip with a
  Cancel/Dismiss button, hosted in the single `MainViewModel.ActiveOperation` slot
  (`src/Duetto/ViewModels/SimpleOperationViewModel.cs`).
- Exit path: `MainWindow.OnClosed` → `MainViewModel.Dispose()`
  (`src/Duetto/Views/MainWindow.axaml.cs:186`, `MainViewModel.cs:642`).

## Design

### Component split

Mirrors the existing injected-seam pattern (`LaunchFile`, `TrashFn`, `DeleteScheduler`).

**1. `PaneViewModel` — dispatch seam only**

Add:

```csharp
// Invoked for a remote file row on activation. MainViewModel wires this to the
// download-and-open orchestration; null in bare unit tests leaves the old no-op.
public Action<FileRowViewModel>? OpenRemoteFile { get; set; }
```

In `Open`, replace the remote no-op:

```csharp
else
{
    if (PathUtil.IsRemote(CurrentPath))
    {
        OpenRemoteFile?.Invoke(row);
        return;
    }
    LaunchFile(row.Entry.FullPath);
}
```

The pane holds no download, temp, or cleanup logic. It only routes.

**2. `RemoteFileOpener` — mechanics (new type)**

A small, UI-free, disposable service that owns download + temp lifetime. Testable with
`InMemoryFileSystemProvider`.

Responsibilities:
- Resolve the provider for a full remote address via the injected `FileSystemRegistry`.
- Stream-copy `provider.OpenRead(localPath)` into
  `<tempRoot>/<guid>/<originalFileName>`, honouring a `CancellationToken`. The original
  file name is preserved so the OS selects the correct application and the file's
  extension/type is intact.
- Track each created `<guid>` directory for later cleanup.
- Launch the downloaded file via an injected `Action<string>` launcher.
- On `Dispose`: best-effort recursive-delete every tracked `<guid>` directory.
- On construction: best-effort delete everything under `tempRoot` (startup sweep).

Sketch (shape, not final):

```csharp
public sealed class RemoteFileOpener : IDisposable
{
    private readonly FileSystemRegistry _registry;
    private readonly Action<string> _launch;
    private readonly string _tempRoot;
    private readonly List<string> _created = new();   // guid dirs
    private readonly object _lock = new();

    public RemoteFileOpener(FileSystemRegistry registry, Action<string> launch, string? tempRoot = null)
    {
        _registry = registry;
        _launch = launch;
        _tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "Duetto", "open");
        SweepRoot();                                  // recover crashed-session leftovers
    }

    // Runs on a background thread (caller schedules it). Copies bytes, returns temp path.
    public string Download(string fullAddress, CancellationToken ct)
    {
        var (provider, localPath) = _registry.Resolve(fullAddress);
        var name = PathUtil.Leaf(localPath);
        var dir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        lock (_lock) _created.Add(dir);
        var target = Path.Combine(dir, name);
        using (var src = provider.OpenRead(localPath))
        using (var dst = File.Create(target))
            CopyWithCancel(src, dst, ct);              // manual buffered loop; sync CopyTo has no ct overload
        return target;
    }

    public void Launch(string tempPath) => _launch(tempPath);

    public void Dispose() { /* best-effort delete each tracked dir */ }
    private void SweepRoot() { /* best-effort recursive delete of _tempRoot contents */ }
}
```

Note: `Guid.NewGuid()` is allowed here (production code); only workflow scripts forbid it.

**3. `MainViewModel` — orchestration + ownership**

- Construct one `RemoteFileOpener`, launching via the pane's `LaunchFile` seam (so a
  single launch mechanism is shared). The opener is created with the default temp root
  in production; tests pass an explicit temp root.
- Wire each pane: `Left.OpenRemoteFile = row => OpenRemoteFile(Left, row);` (and Right).
- Add an injectable background seam mirroring `DeleteScheduler`, so the download runs off
  the UI thread in production and inline in tests:

  ```csharp
  public Func<Action<CancellationToken>, CancellationToken, Task> OpenScheduler { get; set; }
      = static (work, ct) => Task.Run(() => work(ct), ct);
  ```

- Orchestration method:

  ```csharp
  private void OpenRemoteFile(PaneViewModel pane, FileRowViewModel row)
  {
      if (ActiveOperation is { IsFinished: false }) return;       // same guard as copy/delete
      var address = ToAddress(pane.CurrentPath, row.Entry.FullPath);
      var cts = new CancellationTokenSource();
      var op = new SimpleOperationViewModel($"Opening {row.Name}…", cts);
      op.Dismissed += () => { if (ReferenceEquals(ActiveOperation, op)) ActiveOperation = null; op.Dispose(); };
      ActiveOperation = op;

      OpenCompletion = OpenScheduler(ct =>
      {
          string tempPath;
          try { tempPath = _remoteOpener.Download(address, ct); }
          catch (OperationCanceledException) { return; }
          catch { Dispatcher.UIThread.Post(op.Dismiss); return; }   // network/provider error: quiet dismiss
          Dispatcher.UIThread.Post(() =>
          {
              if (cts.IsCancellationRequested) return;
              _remoteOpener.Launch(tempPath);
              op.Finish($"Opened {row.Name}");
          });
      }, cts.Token);
  }
  ```

  `OpenCompletion` (a `Task` property, default `Task.CompletedTask`) exists so tests can
  await the download deterministically, matching `DeleteCompletion`.

- `Dispose`: call `_remoteOpener.Dispose()` alongside the existing teardown.

### Temp layout

```
<OS temp>/Duetto/open/
    <guid-1>/<originalName>       one open
    <guid-2>/<originalName>       another open
```

- Per-open `<guid>` subdir avoids filename collisions while preserving the real filename.
- Root `Duetto/open` is owned entirely by this feature, so sweeping it is safe.

### Cleanup

- **Clean exit:** `MainViewModel.Dispose()` → `RemoteFileOpener.Dispose()` deletes every
  tracked `<guid>` dir. Deletion is best-effort: an external app may still hold the file
  open. On POSIX, unlink of an open file succeeds; on Windows a locked file may fail to
  delete — the exception is swallowed and the leftover is caught by the next startup
  sweep.
- **Startup sweep:** `RemoteFileOpener` constructor best-effort recursive-deletes the
  contents of `Duetto/open`, recovering files a crashed prior session never cleaned.

### Error handling

- Provider/network errors during download (`SshConnectionException`, `IOException`,
  `SocketException`, etc.) are caught in the worker; the strip is dismissed on the UI
  thread and the partial temp is removed. No crash, consistent with existing remote-op
  error handling.
- Cancel via the strip cancels the token; the buffered copy loop throws `OperationCanceledException`,
  the worker returns, and the partial `<guid>` dir is left for cleanup (still tracked, so
  it is removed on exit) — or removed immediately on cancel (implementer's choice; either
  satisfies "deleted on exit").
- Busy slot: if `ActiveOperation` is an unfinished operation, the open is ignored — the
  same rule `StartTransfer` and `DeleteSelected` already apply.

## Testing

**`RemoteFileOpener` (unit, `InMemoryFileSystemProvider` + explicit temp root)**
- Download copies the remote bytes into a temp file whose name equals the source leaf.
- Temp file lives under `<tempRoot>/<guid>/`.
- `Launch` is invoked with the temp path.
- `Dispose` deletes the tracked dirs.
- Constructor sweep deletes pre-existing junk under the temp root.
- Cancelled token aborts the copy and does not launch.

**`PaneViewModel` (Avalonia UI test)**
- Replace `Open_remote_file_row_is_noop_and_does_not_invoke_LaunchFile` (its contract
  inverts): `Open` on a remote file row now invokes the `OpenRemoteFile` hook and does
  not directly call `LaunchFile`, and `CurrentPath` is unchanged.
- Remote directory-row navigation tests remain green (unchanged path).

**`MainViewModel` (Avalonia UI test, inline `OpenScheduler`)**
- Opening a remote file: strip appears, the file is written under the temp root, and the
  launch callback receives the temp path; awaiting `OpenCompletion` is deterministic.
- Busy-slot guard: with an unfinished `ActiveOperation`, opening a remote file is a no-op.

## Files touched

- `src/Duetto/ViewModels/PaneViewModel.cs` — `OpenRemoteFile` seam; remote branch of `Open`.
- `src/Duetto/ViewModels/MainViewModel.cs` — own `RemoteFileOpener`, `OpenScheduler`,
  `OpenCompletion`, orchestration, wire panes, dispose.
- `src/Duetto.Core/...` or `src/Duetto/ViewModels/...` — new `RemoteFileOpener`.
- `tests/Duetto.Tests/...` — new opener tests; update the inverted remote-file test; new
  MainViewModel open test.

## Notes / conventions

- Follows existing injected-seam idioms (`LaunchFile`, `DeleteScheduler`/`DeleteCompletion`)
  rather than introducing new patterns.
- No new NuGet dependencies.
