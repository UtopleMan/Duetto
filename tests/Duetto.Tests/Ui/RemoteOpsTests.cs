using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;

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
}
