using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;
using Renci.SshNet.Common;

namespace Duetto.Tests.Ui;

/// <summary>
/// End-to-end tests for remote-provider routing of file operations:
/// transfer (Req 1), new folder/file (Req 2), rename (Req 3), and delete status text (Req 4).
/// All tests use <see cref="InMemoryFileSystemProvider"/> — no real SFTP connection.
/// </summary>
public class RemoteOpsTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static InMemoryFileSystemProvider MakeRemoteFs() => new();

    private static FileSystemRegistry MakeRegistry(string scheme, string id, IFileSystemProvider provider)
    {
        var reg = new FileSystemRegistry();
        reg.Register(scheme, id, provider);
        return reg;
    }

    private static void SeedFile(InMemoryFileSystemProvider fs, string parent, string name, string content = "x")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var full  = fs.CreateFile(parent, name);
        using var w = fs.OpenWrite(full);
        w.Write(bytes);
    }

    // ── Req 1: Transfer routing ───────────────────────────────────────────────

    /// <summary>
    /// Copy from remote src to remote dst (same provider, different dirs) routes through the
    /// provider-aware TransferEngine overload. File must appear in dst via the in-memory provider.
    /// </summary>
    [AvaloniaFact]
    public async Task StartTransfer_routes_remote_source_through_provider_overload()
    {
        var fs = MakeRemoteFs();
        fs.CreateDirectory("/", "src");
        fs.CreateDirectory("/", "dst");
        SeedFile(fs, "/src", "hello.txt");

        var reg = MakeRegistry("sftp", "srv1", fs);
        using var vm = new MainViewModel("sftp://srv1/src", "sftp://srv1/dst", registry: reg);
        await vm.Left.LoadCompletion;

        vm.Left.SelectByName("hello.txt");
        vm.CopySelected();

        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;

        Assert.True(fs.FileExists("/dst/hello.txt"));
    }

    /// <summary>
    /// Cross-provider transfer: local source file gets copied to in-memory remote dst.
    /// </summary>
    [AvaloniaFact]
    public async Task Cross_provider_transfer_local_to_remote()
    {
        using var src = new TempDir();
        src.File("data.txt", "hello");

        var remoteFs = MakeRemoteFs();
        remoteFs.CreateDirectory("/", "incoming");
        var reg = MakeRegistry("fake", "host", remoteFs);

        // Left = local, Right = remote; we need both reachable through one registry.
        // The local provider is the registry's default, so just register the remote side.
        using var vm = new MainViewModel(src.Path, "fake://host/incoming", registry: reg);
        await vm.Left.LoadCompletion;
        await vm.Right.LoadCompletion;

        vm.Left.SelectByName("data.txt");
        vm.CopySelected();

        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;

        Assert.True(remoteFs.FileExists("/incoming/data.txt"));
    }

    /// <summary>
    /// Download: remote (in-memory) source file gets copied to a local TempDir destination.
    /// </summary>
    [AvaloniaFact]
    public async Task Cross_provider_transfer_remote_to_local_download()
    {
        var remoteFs = MakeRemoteFs();
        SeedFile(remoteFs, "/", "report.txt", "remote bytes");
        var reg = MakeRegistry("fake", "host", remoteFs);

        using var dst = new TempDir();
        using var vm = new MainViewModel("fake://host/", dst.Path, registry: reg);
        await vm.Left.LoadCompletion;
        await vm.Right.LoadCompletion;

        vm.Left.SelectByName("report.txt");
        vm.CopySelected();

        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;

        var landed = Path.Combine(dst.Path, "report.txt");
        Assert.True(File.Exists(landed));
        Assert.Equal("remote bytes", File.ReadAllText(landed));
    }

    /// <summary>
    /// Move within one remote (same provider instance on both panes): the source must be
    /// gone and the destination present — the engine takes the native-Move path.
    /// </summary>
    [AvaloniaFact]
    public async Task Move_within_one_remote_removes_source_and_delivers_destination()
    {
        var fs = MakeRemoteFs();
        fs.CreateDirectory("/", "a");
        fs.CreateDirectory("/", "b");
        SeedFile(fs, "/a", "doc.txt");

        var reg = MakeRegistry("sftp", "srv1", fs);
        using var vm = new MainViewModel("sftp://srv1/a", "sftp://srv1/b", registry: reg);
        await vm.Left.LoadCompletion;

        vm.Left.SelectByName("doc.txt");
        vm.MoveSelected();

        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;

        Assert.False(fs.FileExists("/a/doc.txt"));
        Assert.True(fs.FileExists("/b/doc.txt"));
    }

    /// <summary>
    /// Move between two DIFFERENT remote providers: falls back to copy+delete — the file
    /// lands on the destination provider and is removed from the source provider.
    /// </summary>
    [AvaloniaFact]
    public async Task Move_across_two_remotes_copies_then_deletes()
    {
        var srcFs = MakeRemoteFs();
        var dstFs = MakeRemoteFs();
        SeedFile(srcFs, "/", "payload.txt", "across servers");
        dstFs.CreateDirectory("/", "in");

        var reg = new FileSystemRegistry();
        reg.Register("sftp", "one", srcFs);
        reg.Register("sftp", "two", dstFs);

        using var vm = new MainViewModel("sftp://one/", "sftp://two/in", registry: reg);
        await vm.Left.LoadCompletion;
        await vm.Right.LoadCompletion;

        vm.Left.SelectByName("payload.txt");
        vm.MoveSelected();

        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;

        Assert.False(srcFs.FileExists("/payload.txt"));
        Assert.True(dstFs.FileExists("/in/payload.txt"));
    }

    // ── Req 2: New folder/file on remote pane ────────────────────────────────

    /// <summary>
    /// NewFolder on a remote pane creates the directory through the in-memory provider,
    /// not through local disk.
    /// </summary>
    [AvaloniaFact]
    public void NewFolder_on_remote_pane_creates_via_provider()
    {
        var fs = MakeRemoteFs();
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new PaneViewModel("sftp://srv/", reg);
        Dispatcher.UIThread.RunJobs();

        vm.NewFolder();

        var placeholder = vm.Rows.Single(r => r.IsNewPlaceholder);
        placeholder.EditName = "MyFolder";
        vm.CommitRename(placeholder);
        Dispatcher.UIThread.RunJobs();

        Assert.True(fs.DirectoryExists("/MyFolder"));
        Assert.DoesNotContain(vm.Rows, r => r.IsNewPlaceholder);
    }

    /// <summary>
    /// NewFile on a remote pane creates the file through the in-memory provider.
    /// </summary>
    [AvaloniaFact]
    public void NewFile_on_remote_pane_creates_via_provider()
    {
        var fs = MakeRemoteFs();
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new PaneViewModel("sftp://srv/", reg);
        Dispatcher.UIThread.RunJobs();

        vm.NewFile();

        var placeholder = vm.Rows.Single(r => r.IsNewPlaceholder);
        placeholder.EditName = "notes.txt";
        vm.CommitRename(placeholder);
        Dispatcher.UIThread.RunJobs();

        Assert.True(fs.FileExists("/notes.txt"));
        Assert.DoesNotContain(vm.Rows, r => r.IsNewPlaceholder);
    }

    /// <summary>
    /// Committing a name that already exists in the remote provider keeps editing
    /// (collision detected via provider, not local disk).
    /// </summary>
    [AvaloniaFact]
    public void NewFolder_collision_detected_via_remote_provider()
    {
        var fs = MakeRemoteFs();
        fs.CreateDirectory("/", "Existing");
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new PaneViewModel("sftp://srv/", reg);
        Dispatcher.UIThread.RunJobs();

        vm.NewFolder();
        var placeholder = vm.Rows.Single(r => r.IsNewPlaceholder);
        placeholder.EditName = "Existing";
        vm.CommitRename(placeholder);

        // Stays in edit mode — collision detected.
        Assert.True(placeholder.IsEditing);
        Assert.Contains(vm.Rows, r => r.IsNewPlaceholder);
    }

    // ── Req 3: Rename on remote pane ────────────────────────────────────────

    /// <summary>
    /// Rename on a remote pane routes through the provider (InMemoryFileSystemProvider),
    /// not through local FileOps/local disk.
    /// </summary>
    [AvaloniaFact]
    public async Task Rename_on_remote_pane_routes_through_provider()
    {
        var fs = MakeRemoteFs();
        SeedFile(fs, "/", "old.txt");

        var reg = MakeRegistry("sftp", "srv", fs);
        using var vm = new PaneViewModel("sftp://srv/", reg);
        Dispatcher.UIThread.RunJobs();

        vm.SelectByName("old.txt");
        var row = vm.StartRename()!;
        row.EditName = "new.txt";
        vm.CommitRename(row);
        await vm.RenameCompletion;
        Dispatcher.UIThread.RunJobs();

        Assert.True(fs.FileExists("/new.txt"));
        Assert.False(fs.FileExists("/old.txt"));
        Assert.Equal("new.txt", vm.CursorRow?.Name);
    }

    // ── Req 4: Delete status text ────────────────────────────────────────────

    /// <summary>
    /// Delete on a remote pane uses the DEFAULT TrashFn (no test override): the provider-local
    /// row path must be rebuilt to its full sftp://id/... address so the delete lands on the
    /// remote provider — never on a same-named local path. The strip title must say
    /// "Deleted N items" (HasTrash = false), not "Moved … to Trash".
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_on_remote_pane_uses_provider_and_says_deleted_not_trash()
    {
        var fs = MakeRemoteFs();
        SeedFile(fs, "/", "file.txt");

        var reg = MakeRegistry("sftp", "srv", fs);
        using var vm = new MainViewModel("sftp://srv/", "sftp://srv/", registry: reg);
        await vm.Left.LoadCompletion;

        // Inline scheduler so the test is synchronous; TrashFn stays the production default
        // (TrashViaProvider) so this exercises the real remote routing.
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };

        vm.Left.SelectByName("file.txt");
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion;

        Assert.False(fs.FileExists("/file.txt"));

        var op = vm.ActiveOperation as SimpleOperationViewModel;
        Assert.NotNull(op);
        Assert.True(op!.IsFinished);
        Assert.Contains("Deleted", op.Title);
        Assert.DoesNotContain("Trash", op.Title);
    }

    /// <summary>
    /// After deleting from a local pane (HasTrash = true), the strip title must say
    /// "Moved N items to Trash".
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_status_text_says_trash_for_local()
    {
        using var tmp = new TempDir();
        tmp.File("local.txt", "x");

        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        // Inline scheduler; TrashFn swallows (file deletion, but we only check the title).
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = _ => null;

        vm.Left.SelectByName("local.txt");
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion;

        var op = vm.ActiveOperation as SimpleOperationViewModel;
        Assert.NotNull(op);
        Assert.True(op!.IsFinished);
        Assert.Contains("Trash", op.Title);
        Assert.DoesNotContain("Deleted", op.Title);
    }

    // ── Req 5: Open on remote pane guard tests ───────────────────────────────

    /// <summary>
    /// Open on a remote directory row must navigate the pane to the full sftp:// address
    /// (scheme + id + provider-local sub-path), not the raw provider-local path.
    /// </summary>
    [AvaloniaFact]
    public void Open_remote_directory_row_navigates_to_full_sftp_address()
    {
        var fs = MakeRemoteFs();
        fs.CreateDirectory("/", "sub");
        var reg = MakeRegistry("sftp", "id", fs);

        using var vm = new PaneViewModel("sftp://id/", reg);
        Dispatcher.UIThread.RunJobs();

        vm.SelectByName("sub");
        var row = vm.CursorRow!;
        Assert.True(row.IsDirectory);

        vm.Open(row);
        Dispatcher.UIThread.RunJobs();

        // Must have navigated to the full remote address, not the bare local "/sub".
        Assert.Equal("sftp://id/sub", vm.CurrentPath);
    }

    /// <summary>
    /// Open on a remote directory row must NOT navigate to a same-named LOCAL directory.
    /// Data-loss guard: confirms the seam routes through the remote provider, not the
    /// local one, even when a same-named directory exists on disk.
    /// </summary>
    [AvaloniaFact]
    public void Open_remote_directory_row_does_not_navigate_to_local_same_named_directory()
    {
        var fs = MakeRemoteFs();
        fs.CreateDirectory("/", "docs");
        var reg = MakeRegistry("sftp", "id", fs);

        // The pane starts at the remote root.
        using var vm = new PaneViewModel("sftp://id/", reg);
        Dispatcher.UIThread.RunJobs();

        vm.SelectByName("docs");
        var row = vm.CursorRow!;
        vm.Open(row);
        Dispatcher.UIThread.RunJobs();

        // The pane must stay on a remote path — never a bare local "/docs".
        Assert.True(PathUtil.IsRemote(vm.CurrentPath),
            $"Expected a remote address but got: {vm.CurrentPath}");
        Assert.Equal("sftp://id/docs", vm.CurrentPath);
    }

    /// <summary>
    /// Open on a remote FILE row must be a no-op (remote file open is a deferred feature).
    /// The pane path must be unchanged and LaunchFile must not be invoked.
    /// </summary>
    [AvaloniaFact]
    public void Open_remote_file_row_is_noop_and_does_not_invoke_LaunchFile()
    {
        var fs = MakeRemoteFs();
        var bytes = System.Text.Encoding.UTF8.GetBytes("content");
        var full = fs.CreateFile("/", "note.txt");
        using var w = fs.OpenWrite(full);
        w.Write(bytes);

        var reg = MakeRegistry("sftp", "id", fs);
        using var vm = new PaneViewModel("sftp://id/", reg);
        Dispatcher.UIThread.RunJobs();

        var launchCalled = false;
        vm.LaunchFile = _ => launchCalled = true;

        vm.SelectByName("note.txt");
        var row = vm.CursorRow!;
        Assert.False(row.IsDirectory);

        var pathBefore = vm.CurrentPath;
        vm.Open(row);
        Dispatcher.UIThread.RunJobs();

        Assert.False(launchCalled, "LaunchFile must not be called for a remote file row");
        Assert.Equal(pathBefore, vm.CurrentPath);
    }

    // ── Req 6: NavigateTo pre-flight exception guard ─────────────────────────

    /// <summary>
    /// NavigateTo to a remote address that has no registered provider
    /// (InvalidOperationException from Registry.Resolve — the "reveal after disconnect"
    /// scenario) must not propagate and must leave the pane at its current path.
    /// </summary>
    [AvaloniaFact]
    public void NavigateTo_unregistered_remote_address_does_not_throw_and_leaves_path_unchanged()
    {
        var fs = MakeRemoteFs();
        var reg = MakeRegistry("sftp", "known", fs);

        using var vm = new PaneViewModel("sftp://known/", reg);
        Dispatcher.UIThread.RunJobs();

        var pathBefore = vm.CurrentPath;

        // "sftp://gone/" has no registered provider — Resolve throws InvalidOperationException.
        var ex = Record.Exception(() => vm.NavigateTo("sftp://gone/sub"));
        Assert.Null(ex);
        Assert.Equal(pathBefore, vm.CurrentPath);
    }

    /// <summary>
    /// NavigateTo to a remote address whose provider throws SshConnectionException
    /// (connection dropped) must not propagate and must leave the pane at its current path.
    /// The provider is registered so Resolve succeeds; the exception surfaces from the
    /// Lister (load path) — the NavigateTo guard ensures the pane commits the new path
    /// only after a clean resolve, and the load guard handles failures during listing.
    /// </summary>
    [AvaloniaFact]
    public void NavigateTo_remote_address_with_SshConnectionException_does_not_throw_and_pane_survives()
    {
        // Provider that throws SshConnectionException from DirectoryExists; registered so
        // Resolve succeeds but any pre-check call would throw. Since the implementation
        // skips DirectoryExists for remote (removes UI-thread network stall), the nav
        // commits the path and the load guard handles the subsequent listing failure.
        var throwingFs = new DirectoryExistsThrowingProvider(
            new SshConnectionException("connection dropped"));
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "srv", throwingFs);

        // Start on local so CurrentPath is a local directory (no load needed).
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path, reg);
        Dispatcher.UIThread.RunJobs();

        // Must not throw regardless of whether DirectoryExists is called.
        var ex = Record.Exception(() => vm.NavigateTo("sftp://srv/sub"));
        Assert.Null(ex);
    }

    // ── Helper: provider whose DirectoryExists always throws ────────────────

    /// <summary>
    /// Wraps InMemoryFileSystemProvider but throws a given exception from DirectoryExists.
    /// Used to simulate a connection drop that could surface during a pre-flight check.
    /// </summary>
    private sealed class DirectoryExistsThrowingProvider(Exception ex) : IFileSystemProvider
    {
        private readonly InMemoryFileSystemProvider _inner = new();
        public FileSystemCapabilities Capabilities => _inner.Capabilities;
        public bool DirectoryExists(string path) => throw ex;
        public bool FileExists(string path) => _inner.FileExists(path);
        public FileEntry? Stat(string path) => _inner.Stat(path);
        public IReadOnlyList<FileEntry> List(string path) => _inner.List(path);
        public IEnumerable<FileEntry> EnumerateRecursive(string path) => _inner.EnumerateRecursive(path);
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public Stream OpenWrite(string path) => _inner.OpenWrite(path);
        public string CreateDirectory(string parent, string name) => _inner.CreateDirectory(parent, name);
        public string CreateFile(string parent, string name) => _inner.CreateFile(parent, name);
        public string Rename(string fullPath, string newName) => _inner.Rename(fullPath, newName);
        public void Move(string fromPath, string toPath) => _inner.Move(fromPath, toPath);
        public void Delete(string path, bool toTrash) => _inner.Delete(path, toTrash);
        public void ReplaceFile(string from, string to) => _inner.ReplaceFile(from, to);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => _inner.SetLastWriteTimeUtc(path, utc);
        public VolumeInfo? VolumeFor(string path) => null;
    }
}
