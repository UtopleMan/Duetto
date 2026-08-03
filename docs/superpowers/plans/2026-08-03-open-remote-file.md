# Open Remote File Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pressing Enter (or double-clicking) a remote file row downloads the file to a temp folder, opens it with the OS default app, and deletes the copy on exit.

**Architecture:** A new UI-free `RemoteFileOpener` (in `Duetto.Core`) owns the mechanics — resolve provider, stream-copy remote→`<temp>/Duetto/open/<guid>/<name>`, track temp dirs, clean up on dispose, sweep leftovers on startup. `PaneViewModel.Open` gains an injectable `OpenRemoteFile` seam (mirrors the existing `LaunchFile` seam) that fires only for remote file rows. `MainViewModel` owns one `RemoteFileOpener`, wires both panes' seams, and orchestrates the download behind a `SimpleOperationViewModel` progress strip using the same `await`-scheduler idiom as `DeleteSelected`.

**Tech Stack:** C# / .NET 10, Avalonia MVVM (CommunityToolkit.Mvvm), xUnit + Avalonia.Headless.XUnit for tests.

## Global Constraints

- Target framework: **.NET 10** (`net10.0`). BCL APIs used: `File.SetUnixFileMode`, `File.GetUnixFileMode`, `OperatingSystem.IsWindows()`.
- **No new NuGet dependencies.**
- Build: `dotnet build`. Test: `dotnet test`. Format: `dotnet format`. Solution: `Duetto.slnx`.
- Follow existing injected-seam idioms: `LaunchFile`, `TrashFn`, `DeleteScheduler`/`DeleteCompletion`.
- **Commit messages:** Conventional Commits. **Never** add a `Co-Authored-By` trailer; never mention Claude/Anthropic/AI in any commit, comment, or artifact (project + user rule).
- View-only: the temp copy is never uploaded back; edits are discarded on exit.
- All new C# navigation/verification: prefer the Glider MCP tools over text search.

## File Structure

- **Create** `src/Duetto.Core/FileSystem/RemoteFileOpener.cs` — the download/temp/cleanup service. One responsibility: materialise a remote file locally and manage the temp copy's lifetime.
- **Create** `tests/Duetto.Tests/Core/RemoteFileOpenerTests.cs` — unit tests for the opener (`[Fact]`, no UI).
- **Modify** `src/Duetto/ViewModels/PaneViewModel.cs` — add `OpenRemoteFile` seam; route the remote-file branch of `Open` to it.
- **Modify** `src/Duetto/ViewModels/MainViewModel.cs` — own the `RemoteFileOpener`, add `OpenScheduler`/`OpenCompletion`, wire pane seams, orchestrate download, dispose the opener. Add optional `remoteOpenTempRoot` ctor param for hermetic tests.
- **Modify** `tests/Duetto.Tests/Ui/RemoteOpsTests.cs` — replace the now-inverted `Open_remote_file_row_is_noop_...` test.
- **Modify** `tests/Duetto.Tests/Ui/TransferUiTests.cs` **or add** `tests/Duetto.Tests/Ui/OpenRemoteFileTests.cs` — MainViewModel end-to-end + busy-guard tests. (Plan uses a new file to keep concerns separate.)

---

### Task 1: `RemoteFileOpener` — download, launch, cleanup, startup sweep

**Files:**
- Create: `src/Duetto.Core/FileSystem/RemoteFileOpener.cs`
- Test: `tests/Duetto.Tests/Core/RemoteFileOpenerTests.cs`

**Interfaces:**
- Consumes: `FileSystemRegistry.Resolve(string) -> (IFileSystemProvider Provider, string LocalPath)`; `IFileSystemProvider.OpenRead(string) -> Stream`; `PathUtil.Leaf(string) -> string`.
- Produces:
  - `RemoteFileOpener(FileSystemRegistry registry, Action<string> launch, string? tempRoot = null)` — constructor runs a best-effort sweep of `tempRoot`.
  - `string Download(string fullAddress, CancellationToken ct)` — copies the remote file to a temp path and returns it. (In this task the `ct` is accepted but not yet honored mid-copy; Task 2 makes it cancel.)
  - `void Launch(string path)` — invokes the injected launcher.
  - `void Dispose()` — best-effort deletes every temp dir it created.

- [ ] **Step 1: Write the failing tests (happy path + cleanup + sweep)**

Create `tests/Duetto.Tests/Core/RemoteFileOpenerTests.cs`:

```csharp
using System.Text;
using Duetto.Core.FileSystem;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

public class RemoteFileOpenerTests
{
    private static (FileSystemRegistry Reg, InMemoryFileSystemProvider Fs) RemoteRegistry()
    {
        var fs = new InMemoryFileSystemProvider();
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "srv", fs);
        return (reg, fs);
    }

    private static void Seed(InMemoryFileSystemProvider fs, string parent, string name, string content)
    {
        var full = fs.CreateFile(parent, name);
        using var w = fs.OpenWrite(full);
        w.Write(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public void Download_copies_bytes_to_temp_file_named_after_source()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "hello remote");

        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);

        Assert.Equal("note.txt", Path.GetFileName(path));
        Assert.Equal("hello remote", File.ReadAllText(path));
    }

    [Fact]
    public void Download_places_file_under_temp_root()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);

        Assert.StartsWith(tmp.Path, path);
    }

    [Fact]
    public void Launch_invokes_the_injected_launcher_with_the_temp_path()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        string? launched = null;
        using var opener = new RemoteFileOpener(reg, p => launched = p, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);
        opener.Launch(path);

        Assert.Equal(path, launched);
    }

    [Fact]
    public void Dispose_deletes_downloaded_files()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);
        Assert.True(File.Exists(path));

        opener.Dispose();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Constructor_sweeps_preexisting_temp_root_contents()
    {
        using var tmp = new TempDir();
        var leftover = Path.Combine(tmp.Path, "stale");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "old.txt"), "junk");

        var (reg, _) = RemoteRegistry();
        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);

        Assert.False(Directory.Exists(leftover));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RemoteFileOpenerTests`
Expected: FAIL — `RemoteFileOpener` does not exist (compile error).

- [ ] **Step 3: Implement `RemoteFileOpener`**

Create `src/Duetto.Core/FileSystem/RemoteFileOpener.cs`:

```csharp
namespace Duetto.Core.FileSystem;

// Materialises a remote file to a local temp copy so an external app can open it, and owns
// that copy's lifetime. View-only: copies are never uploaded back and are deleted on Dispose.
// Layout: <tempRoot>/<guid>/<originalName> — a per-open guid dir avoids name collisions and
// keeps the real filename (so the OS picks the right app). tempRoot is owned entirely by this
// type, so the startup sweep can safely clear it (recovers files a crashed session left behind).
public sealed class RemoteFileOpener : IDisposable
{
    private readonly FileSystemRegistry _registry;
    private readonly Action<string> _launch;
    private readonly string _tempRoot;
    private readonly List<string> _created = [];
    private readonly object _lock = new();

    public RemoteFileOpener(FileSystemRegistry registry, Action<string> launch, string? tempRoot = null)
    {
        _registry = registry;
        _launch = launch;
        _tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "Duetto", "open");
        Sweep();
    }

    // Copies the remote file to a fresh temp dir and returns the local path.
    public string Download(string fullAddress, CancellationToken ct)
    {
        var (provider, localPath) = _registry.Resolve(fullAddress);
        var name = PathUtil.Leaf(localPath);
        if (string.IsNullOrEmpty(name))
            name = "file";

        var dir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        lock (_lock)
            _created.Add(dir);

        var target = Path.Combine(dir, name);
        using (var src = provider.OpenRead(localPath))
        using (var dst = File.Create(target))
            src.CopyTo(dst);

        return target;
    }

    public void Launch(string path) => _launch(path);

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var dir in _created)
                TryDelete(dir);
            _created.Clear();
        }
    }

    // Best-effort clear of everything under the temp root — recovers leftovers from a
    // previous session that never got to run Dispose (e.g. a crash).
    private void Sweep()
    {
        try
        {
            if (!Directory.Exists(_tempRoot))
                return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(_tempRoot))
                TryDelete(entry);
        }
        catch
        {
            // Sweep is best-effort; never let cleanup abort startup.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A locked or already-removed file is fine — the next startup sweep catches it.
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~RemoteFileOpenerTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/FileSystem/RemoteFileOpener.cs tests/Duetto.Tests/Core/RemoteFileOpenerTests.cs
git commit -m "feat(core): RemoteFileOpener downloads remote files to temp with cleanup"
```

---

### Task 2: `RemoteFileOpener` — honor cancellation + owner-only temp dirs

**Files:**
- Modify: `src/Duetto.Core/FileSystem/RemoteFileOpener.cs`
- Test: `tests/Duetto.Tests/Core/RemoteFileOpenerTests.cs`

**Interfaces:**
- Consumes: same as Task 1.
- Produces: no signature change. `Download` now throws `OperationCanceledException` when `ct` is cancelled, and each per-open dir is `0700` on POSIX.

- [ ] **Step 1: Write the failing tests (cancel + perms)**

Append to `tests/Duetto.Tests/Core/RemoteFileOpenerTests.cs`:

```csharp
    [Fact]
    public void Cancelled_token_aborts_download_and_does_not_launch()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "hello remote");

        var launched = false;
        using var opener = new RemoteFileOpener(reg, _ => launched = true, tmp.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => opener.Download("sftp://srv/note.txt", cts.Token));
        Assert.False(launched);
    }

    [Fact]
    public void Download_dir_is_owner_only_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes are a no-op on Windows.

        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);

        var mode = File.GetUnixFileMode(Path.GetDirectoryName(path)!);
        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(expected, mode);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RemoteFileOpenerTests`
Expected: `Cancelled_token_...` FAILS (plain `CopyTo` ignores the token, no throw) and `Download_dir_is_owner_only_on_posix` FAILS on macOS/Linux (dir has default mode, not `0700`).

- [ ] **Step 3: Restrict the dir to the owner and copy with cancellation**

In `Download`, add the `RestrictToOwner(dir)` call right after `Directory.CreateDirectory(dir)`:

```csharp
        Directory.CreateDirectory(dir);
        RestrictToOwner(dir);
        lock (_lock)
            _created.Add(dir);
```

Replace the copy line `src.CopyTo(dst);` with `Copy(src, dst, ct);` and add these two private methods:

```csharp
    // Buffered copy that honors cancellation — sync Stream.CopyTo has no CancellationToken overload.
    private static void Copy(Stream src, Stream dst, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = src.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            dst.Write(buffer, 0, read);
        }
    }

    // Downloaded remote files may be sensitive; lock the per-open dir to the owner so a
    // world-readable /tmp (mode 1777 on Linux) cannot leak them to other local users.
    private static void RestrictToOwner(string dir)
    {
        if (OperatingSystem.IsWindows())
            return; // Inherits a private-by-default ACL from the user profile.
        File.SetUnixFileMode(dir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); // 0700
    }
```

- [ ] **Step 4: Run the full opener test file to verify all pass**

Run: `dotnet test --filter FullyQualifiedName~RemoteFileOpenerTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/FileSystem/RemoteFileOpener.cs tests/Duetto.Tests/Core/RemoteFileOpenerTests.cs
git commit -m "feat(core): cancel-aware download and owner-only temp dirs"
```

---

### Task 3: `PaneViewModel` — `OpenRemoteFile` seam

**Files:**
- Modify: `src/Duetto/ViewModels/PaneViewModel.cs` (property near `LaunchFile` at line 90; `Open` remote branch at lines 224-230)
- Test: `tests/Duetto.Tests/Ui/RemoteOpsTests.cs` (replace `Open_remote_file_row_is_noop_and_does_not_invoke_LaunchFile` at line 321)

**Interfaces:**
- Consumes: `FileRowViewModel` (existing).
- Produces: `PaneViewModel.OpenRemoteFile { get; set; }` of type `Action<FileRowViewModel>?`. When a remote file row is activated, `Open` invokes it (and does NOT call `LaunchFile`); when null, the remote branch is a no-op (unchanged old behavior for bare unit tests).

- [ ] **Step 1: Replace the inverted no-op test**

In `tests/Duetto.Tests/Ui/RemoteOpsTests.cs`, replace the whole `Open_remote_file_row_is_noop_and_does_not_invoke_LaunchFile` method (lines ~320-346) with:

```csharp
    [AvaloniaFact]
    public void Open_remote_file_row_invokes_OpenRemoteFile_hook_not_LaunchFile()
    {
        var fs = MakeRemoteFs();
        SeedFile(fs, "/", "note.txt", "content");

        var reg = MakeRegistry("sftp", "id", fs);
        using var vm = new PaneViewModel("sftp://id/", reg);
        Dispatcher.UIThread.RunJobs();

        var launchCalled = false;
        vm.LaunchFile = _ => launchCalled = true;
        FileRowViewModel? hooked = null;
        vm.OpenRemoteFile = row => hooked = row;

        vm.SelectByName("note.txt");
        var row = vm.CursorRow!;
        Assert.False(row.IsDirectory);

        var pathBefore = vm.CurrentPath;
        vm.Open(row);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(row, hooked);
        Assert.False(launchCalled, "LaunchFile must not be called for a remote file row");
        Assert.Equal(pathBefore, vm.CurrentPath);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~RemoteOpsTests.Open_remote_file_row_invokes_OpenRemoteFile_hook_not_LaunchFile`
Expected: FAIL — `PaneViewModel` has no `OpenRemoteFile` (compile error).

- [ ] **Step 3: Add the seam and route the remote branch to it**

In `src/Duetto/ViewModels/PaneViewModel.cs`, directly below the `LaunchFile` property (ends at line 91), add:

```csharp
    // Invoked for a remote file row on activation (Enter / double-click). MainViewModel wires
    // this to the download-and-open orchestration; null leaves the remote branch a no-op.
    public Action<FileRowViewModel>? OpenRemoteFile { get; set; }
```

Replace the remote branch inside `Open` (lines 224-230):

```csharp
        else
        {
            // Remote file open / download-and-open is a deferred feature — no-op until it ships.
            if (PathUtil.IsRemote(CurrentPath))
                return;
            LaunchFile(row.Entry.FullPath);
        }
```

with:

```csharp
        else if (PathUtil.IsRemote(CurrentPath))
        {
            // Remote files can't be launched in place — hand off to the downloader.
            OpenRemoteFile?.Invoke(row);
        }
        else
        {
            LaunchFile(row.Entry.FullPath);
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~RemoteOpsTests.Open_remote_file_row_invokes_OpenRemoteFile_hook_not_LaunchFile`
Expected: PASS. Also run `dotnet test --filter FullyQualifiedName~RemoteOpsTests` — the two remote-directory-navigation tests stay green.

- [ ] **Step 5: Commit**

```bash
git add src/Duetto/ViewModels/PaneViewModel.cs tests/Duetto.Tests/Ui/RemoteOpsTests.cs
git commit -m "feat(ui): route remote file activation through OpenRemoteFile seam"
```

---

### Task 4: `MainViewModel` — orchestrate download, wire panes, dispose

**Files:**
- Modify: `src/Duetto/ViewModels/MainViewModel.cs` (ctor at line 80; fields near `DeleteScheduler`/`DeleteCompletion` at lines 61-64; `Dispose` at line 642)
- Test: Create `tests/Duetto.Tests/Ui/OpenRemoteFileTests.cs`

**Interfaces:**
- Consumes: `RemoteFileOpener(FileSystemRegistry, Action<string>, string?)`, `.Download(string, CancellationToken)`, `.Launch(string)`, `.Dispose()`; `SimpleOperationViewModel(string title, CancellationTokenSource)`; `PaneViewModel.OpenRemoteFile`, `.LaunchFile`, `.CurrentPath`; `FileRowViewModel.Name`, `.Entry.FullPath`; `ToAddress(panePath, rowPath)`.
- Produces:
  - New optional ctor param `string? remoteOpenTempRoot = null` (last param).
  - `MainViewModel.OpenScheduler { get; set; }` of type `Func<Action<CancellationToken>, CancellationToken, Task>` (default `Task.Run`; tests override to run inline).
  - `MainViewModel.OpenCompletion { get; private set; }` of type `Task` (default `Task.CompletedTask`).

- [ ] **Step 1: Write the failing tests (end-to-end + busy guard)**

Create `tests/Duetto.Tests/Ui/OpenRemoteFileTests.cs`:

```csharp
using System.Text;
using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class OpenRemoteFileTests
{
    private static (FileSystemRegistry Reg, InMemoryFileSystemProvider Fs) RemoteRegistry()
    {
        var fs = new InMemoryFileSystemProvider();
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "srv", fs);
        return (reg, fs);
    }

    private static void Seed(InMemoryFileSystemProvider fs, string parent, string name, string content)
    {
        var full = fs.CreateFile(parent, name);
        using var w = fs.OpenWrite(full);
        w.Write(Encoding.UTF8.GetBytes(content));
    }

    [AvaloniaFact]
    public async Task Open_remote_file_downloads_to_temp_and_launches_it()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "hello remote");

        using var vm = new MainViewModel(
            "sftp://srv/", "sftp://srv/", registry: reg, remoteOpenTempRoot: tmp.Path);
        await vm.Left.LoadCompletion;

        string? launched = null;
        vm.Left.LaunchFile = p => launched = p;
        vm.OpenScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };

        vm.Left.SelectByName("note.txt");
        vm.Left.Open(vm.Left.CursorRow!);
        await vm.OpenCompletion;

        Assert.NotNull(launched);
        Assert.Equal("note.txt", Path.GetFileName(launched!));
        Assert.Equal("hello remote", File.ReadAllText(launched!));
        Assert.StartsWith(tmp.Path, launched!);
    }

    [AvaloniaFact]
    public async Task Open_remote_file_is_noop_while_an_operation_is_running()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        using var vm = new MainViewModel(
            "sftp://srv/", "sftp://srv/", registry: reg, remoteOpenTempRoot: tmp.Path);
        await vm.Left.LoadCompletion;

        var launchCount = 0;
        vm.Left.LaunchFile = _ => launchCount++;
        // First open never completes — leaves ActiveOperation unfinished.
        var gate = new TaskCompletionSource();
        vm.OpenScheduler = (_, _) => gate.Task;

        vm.Left.SelectByName("note.txt");
        vm.Left.Open(vm.Left.CursorRow!);
        var first = vm.ActiveOperation;
        Assert.NotNull(first);
        Assert.False(first!.IsFinished);

        // Second open must be ignored while the first is in flight.
        vm.Left.Open(vm.Left.CursorRow!);

        Assert.Same(first, vm.ActiveOperation);
        Assert.Equal(0, launchCount);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~OpenRemoteFileTests`
Expected: FAIL — `MainViewModel` has no `remoteOpenTempRoot` param / `OpenScheduler` / `OpenCompletion` (compile error).

- [ ] **Step 3: Add scheduler seam + completion property**

In `src/Duetto/ViewModels/MainViewModel.cs`, directly below `DeleteCompletion` (line 64) add:

```csharp
    // Runs the remote-file download off the UI thread; tests swap in an inline runner so the
    // download and launch complete deterministically. Mirrors DeleteScheduler.
    public Func<Action<CancellationToken>, CancellationToken, Task> OpenScheduler { get; set; }
        = static (work, ct) => Task.Run(() => work(ct), ct);

    public Task OpenCompletion { get; private set; } = Task.CompletedTask;

    private readonly RemoteFileOpener _remoteOpener;
```

- [ ] **Step 4: Add the ctor param and construct + wire the opener**

Change the constructor signature (line 80-93) to add a final parameter:

```csharp
        S3ConnectionManager? s3ConnectionManager = null,
        S3ConnectionStore? s3ConnectionStore = null,
        string? remoteOpenTempRoot = null)
```

In the constructor body, after `Right = new PaneViewModel(rightPath, Registry);` (line 109), add:

```csharp
        _remoteOpener = new RemoteFileOpener(Registry, p => Left.LaunchFile(p), remoteOpenTempRoot);
        Left.OpenRemoteFile = row => StartRemoteFileOpen(Left, row);
        Right.OpenRemoteFile = row => StartRemoteFileOpen(Right, row);
```

- [ ] **Step 5: Add the orchestration methods**

In `src/Duetto/ViewModels/MainViewModel.cs`, add these two methods next to `RunDeleteAsync` (after the `RunDeleteAsync` method, around line 632):

```csharp
    // Enter / double-click on a remote file row: download to temp behind a progress strip,
    // then launch the local copy. Same single-slot guard as copy/delete.
    private void StartRemoteFileOpen(PaneViewModel pane, FileRowViewModel row)
    {
        if (ActiveOperation is { IsFinished: false })
            return;

        var address = ToAddress(pane.CurrentPath, row.Entry.FullPath);
        var cts = new CancellationTokenSource();
        var op = new SimpleOperationViewModel($"Opening {row.Name}…", cts);
        op.Dismissed += () =>
        {
            if (ReferenceEquals(ActiveOperation, op))
                ActiveOperation = null;
            op.Dispose();
        };
        ActiveOperation = op;

        OpenCompletion = RunOpenAsync(address, row.Name, op, cts.Token);
    }

    // The await resumes on the captured UI context (like RunDeleteAsync), so Launch/Finish run
    // on the UI thread. A failed download dismisses the strip quietly — the app never crashes.
    private async Task RunOpenAsync(
        string address, string name, SimpleOperationViewModel op, CancellationToken token)
    {
        string? tempPath = null;
        var failed = false;
        try
        {
            await OpenScheduler(ct => tempPath = _remoteOpener.Download(address, ct), token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or SshException
            or SocketException
            or HostKeyChangedException)
        {
            failed = true;
        }

        if (failed || token.IsCancellationRequested || tempPath is null)
        {
            op.Dismiss();
            return;
        }

        _remoteOpener.Launch(tempPath);
        op.Finish($"Opened {name}");
    }
```

Note: `IOException`, `UnauthorizedAccessException`, `InvalidOperationException` resolve via existing global usings; `SshException` + `HostKeyChangedException` via the file's `Renci.SshNet.Common` / `Duetto.Core.Remote` usings; `SocketException` via `System.Net.Sockets` (already imported at line 1). No new usings needed.

- [ ] **Step 6: Dispose the opener**

In `Dispose` (line 642), add `_remoteOpener.Dispose();` alongside the other teardown:

```csharp
        ActiveOperation?.Dispose();
        Left.Dispose();
        Right.Dispose();
        ConnectionManager.Dispose();
        SmbConnectionManager.Dispose();
        S3ConnectionManager.Dispose();
        _remoteOpener.Dispose();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~OpenRemoteFileTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add src/Duetto/ViewModels/MainViewModel.cs tests/Duetto.Tests/Ui/OpenRemoteFileTests.cs
git commit -m "feat(ui): download-and-open remote files with progress and exit cleanup"
```

---

### Task 5: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: builds with no errors.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass, including the pre-existing suites (`RemoteOpsTests`, `DeleteOperationTests`, `TransferUiTests`).

- [ ] **Step 3: Format check**

Run: `dotnet format --verify-no-changes`
Expected: no formatting changes required. If it reports changes, run `dotnet format` and commit them:

```bash
git add -A
git commit -m "style: dotnet format"
```

- [ ] **Step 4: Manual smoke (optional, requires a live remote)**

Connect to an SFTP/SMB/S3 remote, move the cursor to a file, press Enter. Expect: a brief "Opening <name>…" strip, the file opens in the OS default app. Quit Duetto; confirm `<OS temp>/Duetto/open` is empty.

---

## Notes / conventions

- `Guid.NewGuid()` is used in production `RemoteFileOpener` — allowed (only workflow scripts forbid it).
- The opener launches via `p => Left.LaunchFile(p)`; both panes share the same launcher default (`Process.Start`), and tests that set `vm.Left.LaunchFile` are honored because the closure reads the property at call time.
- No edit-back: the temp copy is view-only. This is intentional and confirmed with the user.
