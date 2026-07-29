using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

/// <summary>
/// End-to-end tests for Task M: remote search correctness (reveal, delete-from-search,
/// capability gating, HostKeyChanged mid-op, transfer-strip display address).
/// All tests use <see cref="InMemoryFileSystemProvider"/> — no real SFTP connection.
/// </summary>
public class RemoteSearchTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static InMemoryFileSystemProvider MakeRemoteFs(
        bool canRename = true,
        bool canCreateEmptyDir = true,
        bool canCreateFile = true,
        bool canDelete = true,
        bool supportsSearch = true)
        => new()
        {
            Capabilities = new FileSystemCapabilities
            {
                CanRename = canRename,
                CanCreateEmptyDir = canCreateEmptyDir,
                CanCreateFile = canCreateFile,
                CanDelete = canDelete,
                HasTrash = false,
                HasPermissions = true,
                PreservesMTime = true,
                AtomicRename = true,
                CanWatch = false,
                ReportsCapacity = false,
                SupportsSearch = supportsSearch,
                CaseSensitive = true,
                Separator = '/',
            },
        };

    private static FileSystemRegistry MakeRegistry(string scheme, string id, IFileSystemProvider provider)
    {
        var reg = new FileSystemRegistry();
        reg.Register(scheme, id, provider);
        return reg;
    }

    private static void SeedFile(InMemoryFileSystemProvider fs, string parent, string name, string content = "x")
    {
        var full = fs.CreateFile(parent, name);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        using var w = fs.OpenWrite(full);
        w.Write(bytes);
    }

    // ── 1. Reveal navigates to the remote parent ─────────────────────────────

    /// <summary>
    /// RevealRequested on a remote search hit must navigate the left pane to the remote
    /// parent directory (sftp://id/docs), not a bare local path (/docs). Before the fix
    /// the handler used Path.GetDirectoryName which returned a local-looking "/docs" and
    /// NavigateTo resolved against the local provider.
    /// </summary>
    [AvaloniaFact]
    public async Task Reveal_remote_hit_navigates_left_pane_to_remote_parent()
    {
        var fs = MakeRemoteFs();
        fs.CreateDirectory("/", "docs");
        SeedFile(fs, "/docs", "target.txt");
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new MainViewModel("sftp://srv/", "sftp://srv/", registry: reg);
        await vm.Left.LoadCompletion;

        vm.Search.Query = "target";
        await vm.Search.StartSearchAsync();

        Assert.Single(vm.Search.Results);
        vm.Search.Selection.Select(0);
        vm.Search.RevealSelected();

        // The left pane must navigate to the remote parent, not a bare "/docs" path.
        Assert.Equal("sftp://srv/docs", vm.Left.CurrentPath);
        Assert.Equal("target.txt", (vm.Left.Selection.SelectedItem as FileRowViewModel)?.Name);
        Assert.True(vm.Left.IsActive);
    }

    // ── 2. Delete-from-search hits the remote provider (data-loss guard) ────

    /// <summary>
    /// Deleting a remote search hit must delete from the REMOTE provider — not from a
    /// same-named local path. Before the fix, if /docs/secret.txt existed locally AND on
    /// the remote, the delete could silently wipe the local copy.
    ///
    /// Setup: remote file at /docs/secret.txt only (no local copy with that name).
    /// After delete: remote file gone; the search-result row removed.
    /// </summary>
    [AvaloniaFact]
    public async Task Delete_from_search_removes_file_from_remote_provider()
    {
        var fs = MakeRemoteFs();
        fs.CreateDirectory("/", "docs");
        SeedFile(fs, "/docs", "secret.txt");
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new MainViewModel("sftp://srv/", "sftp://srv/", registry: reg);
        await vm.Left.LoadCompletion;

        vm.Search.Query = "secret";
        await vm.Search.StartSearchAsync();
        Assert.Single(vm.Search.Results);
        vm.Search.Selection.Select(0);

        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.DeleteSelected();
        await vm.DeleteCompletion;

        Assert.False(fs.FileExists("/docs/secret.txt"));
        Assert.Empty(vm.Search.Results);
    }

    // ── 3. SupportsSearch=false → IsSearchSupported=false ────────────────────

    /// <summary>
    /// When the active pane's provider returns SupportsSearch=false, IsSearchSupported
    /// must be false after RefreshSearchSupported is called (triggered by pane switch).
    /// </summary>
    [AvaloniaFact]
    public async Task IsSearchSupported_is_false_when_provider_does_not_support_search()
    {
        var fs = MakeRemoteFs(supportsSearch: false);
        var reg = MakeRegistry("sftp", "nosearch", fs);

        using var vm = new MainViewModel("sftp://nosearch/", "sftp://nosearch/", registry: reg);
        await vm.Left.LoadCompletion;

        // RefreshSearchSupported is called in StartSearchAsync and on ActivePane change.
        await vm.Search.StartSearchAsync();

        // After starting a search on a no-search provider, it should be false.
        // (Query is empty so no actual search runs, but we can call it directly.)
        vm.Search.Query = "anything";
        vm.Search.RefreshSearchSupported();

        Assert.False(vm.Search.IsSearchSupported);
    }

    [AvaloniaFact]
    public void IsSearchSupported_updates_when_active_pane_switches_to_unsupported_provider()
    {
        var noSearchFs = MakeRemoteFs(supportsSearch: false);
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "nosearch", noSearchFs);
        // Right pane is local (default registry handles it), which supports search.
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, "sftp://nosearch/", registry: reg);
        Dispatcher.UIThread.RunJobs();

        // Initially on local Left pane → search supported.
        Assert.True(vm.Search.IsSearchSupported);

        // Switch to the remote no-search pane.
        vm.Activate(vm.Right);

        Assert.False(vm.Search.IsSearchSupported);

        // Switch back to local pane → supported again.
        vm.Activate(vm.Left);
        Assert.True(vm.Search.IsSearchSupported);
    }

    [AvaloniaFact]
    public async Task SupportsSearch_false_yields_no_results()
    {
        var fs = MakeRemoteFs(supportsSearch: false);
        SeedFile(fs, "/", "needle.txt");
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new MainViewModel("sftp://srv/", "sftp://srv/", registry: reg);
        await vm.Left.LoadCompletion;

        vm.Search.Query = "needle";
        await vm.Search.StartSearchAsync();

        Assert.Empty(vm.Search.Results);
    }

    // ── 4. Capability gating — F2/New/Delete no-op ──────────────────────────

    [AvaloniaFact]
    public void StartRename_noops_when_CanRename_is_false()
    {
        var fs = MakeRemoteFs(canRename: false);
        SeedFile(fs, "/", "file.txt");
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new PaneViewModel("sftp://srv/", reg);
        Dispatcher.UIThread.RunJobs();

        vm.SelectByName("file.txt");
        var result = vm.StartRename();

        Assert.Null(result);
        // The row must not be in edit mode.
        var row = vm.Rows.FirstOrDefault(r => r.Name == "file.txt");
        Assert.NotNull(row);
        Assert.False(row!.IsEditing);
    }

    [AvaloniaFact]
    public void NewFolder_noops_when_CanCreateEmptyDir_is_false()
    {
        var fs = MakeRemoteFs(canCreateEmptyDir: false);
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new PaneViewModel("sftp://srv/", reg);
        Dispatcher.UIThread.RunJobs();

        var rowCountBefore = vm.Rows.Count;
        vm.NewFolder();

        // No placeholder should have been inserted.
        Assert.Equal(rowCountBefore, vm.Rows.Count);
        Assert.DoesNotContain(vm.Rows, r => r.IsNewPlaceholder);
    }

    [AvaloniaFact]
    public void NewFile_noops_when_CanCreateFile_is_false()
    {
        var fs = MakeRemoteFs(canCreateFile: false);
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new PaneViewModel("sftp://srv/", reg);
        Dispatcher.UIThread.RunJobs();

        var rowCountBefore = vm.Rows.Count;
        vm.NewFile();

        Assert.Equal(rowCountBefore, vm.Rows.Count);
        Assert.DoesNotContain(vm.Rows, r => r.IsNewPlaceholder);
    }

    [AvaloniaFact]
    public async Task DeleteSelected_noops_when_CanDelete_is_false()
    {
        var fs = MakeRemoteFs(canDelete: false);
        SeedFile(fs, "/", "keep.txt");
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new MainViewModel("sftp://srv/", "sftp://srv/", registry: reg);
        await vm.Left.LoadCompletion;

        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.Left.SelectByName("keep.txt");
        vm.DeleteSelected();
        // No delete operation should have started.
        Assert.Null(vm.ActiveOperation);
        // File must still exist.
        Assert.True(fs.FileExists("/keep.txt"));
    }

    // ── 5. Mid-op HostKeyChangedException → transfer faults with clear status ─

    /// <summary>
    /// A provider that throws HostKeyChangedException from OpenRead mid-transfer must
    /// cause the transfer session to fault. The TransferViewModel title should contain
    /// the fault message (not just "Copying cancelled").
    /// </summary>
    [AvaloniaFact]
    public async Task Transfer_faults_with_message_on_HostKeyChanged()
    {
        var srcFs = new ThrowingProvider(new HostKeyChangedException(
            "srv", "old-fp", "new-fp", "ssh-ed25519", "ssh-ed25519:[srv]:22"));
        var dstFs = MakeRemoteFs();
        // Register both under the same registry; srcFs handles "sftp://src/".
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "src", srcFs);
        reg.Register("sftp", "dst", dstFs);

        using var vm = new MainViewModel("sftp://src/", "sftp://dst/", registry: reg);
        await vm.Left.LoadCompletion;

        // Manually trigger a transfer via the engine (source path is provider-local).
        var session = TransferEngine.Start(
            ["/file.txt"],
            srcFs,
            "/",
            dstFs,
            TransferMode.Copy);

        await session.Completion;
        var snap = session.Snapshot();

        Assert.True(snap.IsComplete);
        Assert.NotNull(snap.FaultMessage);
        Assert.Contains("Host key changed", snap.FaultMessage);
        Assert.Contains("srv", snap.FaultMessage);
        Assert.Contains("reconnect", snap.FaultMessage);
    }

    [AvaloniaFact]
    public async Task TransferViewModel_title_shows_fault_message_on_HostKeyChanged()
    {
        var srcFs = new ThrowingProvider(new HostKeyChangedException(
            "myhost", "old", "new", "ssh-rsa", "ssh-rsa:[myhost]:22"));
        var dstFs = MakeRemoteFs();
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "src", srcFs);
        reg.Register("sftp", "dst", dstFs);

        using var vm = new MainViewModel("sftp://src/", "sftp://dst/", registry: reg);

        var session = TransferEngine.Start(
            ["/file.txt"],
            srcFs,
            "/",
            dstFs,
            TransferMode.Copy);

        await session.Completion;

        var transferVm = new TransferViewModel(session, null);
        transferVm.UpdateNow();

        Assert.Contains("Host key changed", transferVm.Title);
        Assert.Contains("reconnect", transferVm.Title);
    }

    // ── 6. Transfer strip shows full address for remote destination ──────────

    /// <summary>
    /// When a transfer targets a remote directory (sftp://srv/incoming), the transfer strip
    /// title must show the full sftp:// address, not just the provider-local path (/incoming).
    /// Before the fix, StartTransfer passed destLocalDir (provider-local) to TransferEngine.Start,
    /// so the strip showed "/incoming" instead of "sftp://srv/incoming".
    /// </summary>
    [AvaloniaFact]
    public async Task Transfer_strip_shows_full_sftp_address_as_destination()
    {
        var fs = MakeRemoteFs();
        SeedFile(fs, "/", "data.txt");
        fs.CreateDirectory("/", "incoming");
        var reg = MakeRegistry("sftp", "srv", fs);

        using var vm = new MainViewModel("sftp://srv/", "sftp://srv/incoming", registry: reg);
        await vm.Left.LoadCompletion;

        vm.Left.SelectByName("data.txt");
        vm.CopySelected();

        Assert.NotNull(vm.ActiveTransfer);
        var transfer = vm.ActiveTransfer!;

        // Title is set in the constructor from DestinationDir.
        Assert.Contains("sftp://srv/incoming", transfer.Title);
        Assert.DoesNotContain("Copying to /incoming", transfer.Title);

        await transfer.Session.Completion;
        transfer.UpdateNow();

        // After completion the title becomes "Copied to <dest>".
        Assert.Contains("sftp://srv/incoming", transfer.Title);
    }

    // ── Helper: provider that throws on OpenRead ─────────────────────────────

    /// <summary>
    /// Wraps an in-memory provider but throws a given exception from <see cref="OpenRead"/>.
    /// Used to simulate a mid-transfer HostKeyChangedException.
    /// </summary>
    private sealed class ThrowingProvider(Exception ex) : IFileSystemProvider
    {
        private readonly InMemoryFileSystemProvider _inner = new();

        public FileSystemCapabilities Capabilities => _inner.Capabilities;

        public bool DirectoryExists(string path) => path == "/";
        public bool FileExists(string path) => path == "/file.txt";
        public FileEntry? Stat(string path) => path == "/file.txt"
            ? new FileEntry
            {
                Name = "file.txt", FullPath = "/file.txt", IsDirectory = false,
                SizeBytes = 1, TypeLabel = "File", ModifiedUtc = DateTime.UtcNow,
                UnixPermissions = "", AccessSummary = "R",
            }
            : null;

        public IReadOnlyList<FileEntry> List(string path) => path == "/"
            ? [Stat("/file.txt")!]
            : [];

        public IEnumerable<FileEntry> EnumerateRecursive(string path)
        {
            if (Stat("/file.txt") is { } e) yield return e;
        }

        // Throws the injected exception to simulate a mid-transfer failure.
        public Stream OpenRead(string path) => throw ex;

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
