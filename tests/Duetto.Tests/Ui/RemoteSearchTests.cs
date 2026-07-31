using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class RemoteSearchTests
{
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

        Assert.Equal("sftp://srv/docs", vm.Left.CurrentPath);
        Assert.Equal("target.txt", (vm.Left.Selection.SelectedItem as FileRowViewModel)?.Name);
        Assert.True(vm.Left.IsActive);
    }

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

    [AvaloniaFact]
    public async Task IsSearchSupported_is_false_when_provider_does_not_support_search()
    {
        var fs = MakeRemoteFs(supportsSearch: false);
        var reg = MakeRegistry("sftp", "nosearch", fs);

        using var vm = new MainViewModel("sftp://nosearch/", "sftp://nosearch/", registry: reg);
        await vm.Left.LoadCompletion;

        await vm.Search.StartSearchAsync();

        vm.Search.Query = "anything";
        vm.Search.RefreshSearchSupported();

        Assert.False(vm.Search.IsSearchSupported);
    }

    [AvaloniaFact]
    public void IsSearchSupported_is_false_at_construction_when_initial_pane_has_no_search()
    {
        var fs = MakeRemoteFs(supportsSearch: false);
        var reg = MakeRegistry("sftp", "nosearch", fs);

        using var vm = new MainViewModel("sftp://nosearch/", "sftp://nosearch/", registry: reg);

        Assert.False(vm.Search.IsSearchSupported);
    }

    [AvaloniaFact]
    public void IsSearchSupported_updates_when_active_pane_switches_to_unsupported_provider()
    {
        var noSearchFs = MakeRemoteFs(supportsSearch: false);
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "nosearch", noSearchFs);
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, "sftp://nosearch/", registry: reg);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Search.IsSearchSupported);

        vm.Activate(vm.Right);

        Assert.False(vm.Search.IsSearchSupported);

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
        Assert.Null(vm.ActiveOperation);
        Assert.True(fs.FileExists("/keep.txt"));
    }

    [AvaloniaFact]
    public async Task Transfer_faults_with_message_on_HostKeyChanged()
    {
        var srcFs = new ThrowingProvider(new HostKeyChangedException(
            "srv", "old-fp", "new-fp", "ssh-ed25519", "ssh-ed25519:[srv]:22"));
        var dstFs = MakeRemoteFs();
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "src", srcFs);
        reg.Register("sftp", "dst", dstFs);

        using var vm = new MainViewModel("sftp://src/", "sftp://dst/", registry: reg);
        await vm.Left.LoadCompletion;

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

        Assert.Contains("sftp://srv/incoming", transfer.Title);
        Assert.DoesNotContain("Copying to /incoming", transfer.Title);

        await transfer.Session.Completion;
        transfer.UpdateNow();

        Assert.Contains("sftp://srv/incoming", transfer.Title);
    }

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
