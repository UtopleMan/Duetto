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
    /// After deleting from a remote pane (HasTrash = false), the strip title must say
    /// "Deleted N items", not "Moved … to Trash".
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_status_text_says_deleted_not_trash_for_remote()
    {
        var fs = MakeRemoteFs();
        SeedFile(fs, "/", "file.txt");

        var reg = MakeRegistry("sftp", "srv", fs);
        using var vm = new MainViewModel("sftp://srv/", "sftp://srv/", registry: reg);
        await vm.Left.LoadCompletion;

        // Use inline scheduler so the test is synchronous.
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        // TrashFn via provider (default TrashViaProvider) calls provider.Delete which always
        // deletes permanently for remote. Register an explicit override to route through registry.
        vm.TrashFn = path =>
        {
            var (provider, localPath) = reg.Resolve(path);
            provider.Delete(localPath, toTrash: false);
            return null;
        };

        vm.Left.SelectByName("file.txt");
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion;

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
