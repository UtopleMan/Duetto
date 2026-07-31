using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;
using Renci.SshNet.Common;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Ui;

public sealed class ConnectToShareTests
{
    private static StoredConnection MakeStored(
        string id = "conn1",
        string name = "My Server",
        string host = "example.com",
        string initialPath = "/",
        bool savePassword = false,
        string obfuscated = "") =>
        new StoredConnection
        {
            Id = id,
            Name = name,
            Host = host,
            Port = 22,
            Username = "user",
            AuthMode = AuthMode.Password,
            InitialRemotePath = initialPath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };

    private sealed class FixedAdapterFactory(FakeSftpClientAdapter adapter) : ISftpClientFactory
    {
        public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => adapter;
    }

    [AvaloniaFact]
    public void Already_connected_navigates_immediately_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter();
        // Pre-create the target directory in the adapter tree so DirectoryExists returns true.
        adapter.CreateDirectory("/home");
        adapter.CreateDirectory("/home/user");

        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(adapter));
        manager.Connect(new DuettoConnectionInfo("srv1", "My Server", "fake.local"), ConnectSecret.FromPassword("pw"));

        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager, connectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredConnection?>();
        vm.OpenConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        var stored = MakeStored("srv1", initialPath: "/home/user");
        vm.ConnectToShare(stored, vm.Left);

        Assert.Equal("sftp://srv1/home/user", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_succeeds_navigates_and_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter();
        // Pre-create the target directory in the adapter tree so DirectoryExists returns true.
        adapter.CreateDirectory("/projects");

        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(adapter));

        var codec = new SecretCodec();
        var obfuscated = codec.Encrypt("testpw");
        var stored = MakeStored("srv1", initialPath: "/projects", savePassword: true, obfuscated: obfuscated);
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { stored });
        var store = new ConnectionStore(":mem:", _ => json, (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager, connectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredConnection?>();
        vm.OpenConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("sftp://srv1/projects", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_throws_SshAuthenticationException_opens_dialog_no_navigate()
    {
        using var tmp = new TempDir();
        var codec = new SecretCodec();
        var obfuscated = codec.Encrypt("badpw");
        var stored = MakeStored("srv1", initialPath: "/", savePassword: true, obfuscated: obfuscated);

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter
        {
            NextConnectThrow = new SshAuthenticationException("Bad password"),
        };
        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(adapter));

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { stored });
        var store = new ConnectionStore(":mem:", _ => json, (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager, connectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredConnection?>();
        vm.OpenConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        var pathBefore = vm.Left.CurrentPath;
        vm.ConnectToShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(dialogsOpened);
        Assert.Equal(pathBefore, vm.Left.CurrentPath);
    }

    [AvaloniaFact]
    public void Connect_throws_SshOperationTimeoutException_opens_dialog_no_navigate()
    {
        using var tmp = new TempDir();
        var codec = new SecretCodec();
        var obfuscated = codec.Encrypt("pw");
        var stored = MakeStored("srv1", initialPath: "/", savePassword: true, obfuscated: obfuscated);

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter
        {
            NextConnectThrow = new SshOperationTimeoutException("Timed out"),
        };
        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(adapter));

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { stored });
        var store = new ConnectionStore(":mem:", _ => json, (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager, connectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredConnection?>();
        vm.OpenConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        var pathBefore = vm.Left.CurrentPath;
        vm.ConnectToShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(dialogsOpened);
        Assert.Equal(pathBefore, vm.Left.CurrentPath);
    }

    [AvaloniaFact]
    public void Connect_throws_IOException_opens_dialog_no_navigate()
    {
        using var tmp = new TempDir();
        var codec = new SecretCodec();
        var obfuscated = codec.Encrypt("pw");
        var stored = MakeStored("srv1", initialPath: "/", savePassword: true, obfuscated: obfuscated);

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter
        {
            NextConnectThrow = new IOException("Network unreachable"),
        };
        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(adapter));

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { stored });
        var store = new ConnectionStore(":mem:", _ => json, (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager, connectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredConnection?>();
        vm.OpenConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        var pathBefore = vm.Left.CurrentPath;
        vm.ConnectToShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(dialogsOpened);
        Assert.Equal(pathBefore, vm.Left.CurrentPath);
    }

    [AvaloniaFact]
    public void No_secret_opens_dialog_prefilled()
    {
        using var tmp = new TempDir();
        var stored = MakeStored("srv1", savePassword: false);

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter();
        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(adapter));

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { stored });
        var store = new ConnectionStore(":mem:", _ => json, (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager, connectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredConnection?>();
        vm.OpenConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToShare(stored, vm.Left);

        Assert.Single(dialogsOpened);
        Assert.Equal("srv1", dialogsOpened[0]!.Id);
    }

    [AvaloniaFact]
    public void Connect_failure_invokes_the_dialog_seam_captured_at_call_time()
    {
        using var tmp = new TempDir();
        var codec = new SecretCodec();
        var obfuscated = codec.Encrypt("badpw");
        var stored = MakeStored("srv1", initialPath: "/", savePassword: true, obfuscated: obfuscated);

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter
        {
            NextConnectThrow = new SshAuthenticationException("Bad password"),
        };
        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(adapter));

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { stored });
        var store = new ConnectionStore(":mem:", _ => json, (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager, connectionStore: store, codec: codec);

        // Deferred scheduler: capture the connect work without running it, so we can
        // overwrite the seam between the ConnectToShare call and the connect failure.
        Action? capturedWork = null;
        vm.ConnectScheduler = work => { capturedWork = work; return Task.CompletedTask; };

        var originalInvoked = 0;
        var laterInvoked = 0;
        vm.OpenConnectDialog = (_, _) => originalInvoked++;

        vm.ConnectToShare(stored, vm.Left);
        Assert.NotNull(capturedWork);

        // Simulate a second share click rewiring the seam (each call site wires its own owner).
        vm.OpenConnectDialog = (_, _) => laterInvoked++;

        // Now the first connect fails: it must open the dialog wired for the FIRST click.
        capturedWork!();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, originalInvoked);
        Assert.Equal(0, laterInvoked);
    }
}

public sealed class PaneLoadRobustnessTests
{
    [AvaloniaFact]
    public void Load_catch_SshException_shows_empty_rows_and_resets_IsLoading()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        // The registered provider only satisfies NavigateTo's DirectoryExists check during
        // pane construction; the Lister override below is what actually throws under test.
        registry.Register("sftp", "srv1", new ThrowingProvider(new SshException("SFTP error")));

        using var pane = new PaneViewModel("sftp://srv1/", registry);
        pane.Lister = _ => throw new SshException("SFTP error");

        pane.Reload(preserveSelection: false);

        // At remote root there is no parent nav row.
        Assert.False(pane.IsLoading);
        Assert.Empty(pane.Rows);
    }

    [AvaloniaFact]
    public void Load_catch_InvalidOperationException_shows_empty_rows_and_resets_IsLoading()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        // As above: the registered provider only satisfies the existence check;
        // the Lister override does the throwing.
        registry.Register("sftp", "srv2", new ThrowingProvider(new InvalidOperationException("Registry removed")));

        using var pane = new PaneViewModel("sftp://srv2/", registry);
        pane.Lister = _ => throw new InvalidOperationException("Registry removed");

        pane.Reload(preserveSelection: false);

        Assert.False(pane.IsLoading);
        Assert.Empty(pane.Rows);
    }

    // Minimal provider that DirectoryExists=true but List throws.
    private sealed class ThrowingProvider(Exception ex) : IFileSystemProvider
    {
        public FileSystemCapabilities Capabilities { get; } = new()
        {
            CanRename = false, CanCreateEmptyDir = false, CanCreateFile = false,
            CanDelete = false, HasTrash = false, HasPermissions = false,
            PreservesMTime = false, AtomicRename = false, CanWatch = false,
            ReportsCapacity = false, SupportsSearch = false, CaseSensitive = true,
            Separator = '/',
        };

        public IReadOnlyList<FileEntry> List(string path) => throw ex;
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
        public FileEntry? Stat(string path) => null;
        public string CreateDirectory(string parent, string name) => parent + "/" + name;
        public string CreateFile(string parent, string name) => parent + "/" + name;
        public string Rename(string fullPath, string newName) => fullPath;
        public void Delete(string path, bool toTrash) { }
        public void ReplaceFile(string from, string to) { }
        public void Move(string fromPath, string toPath) { }
        public Stream OpenRead(string path) => Stream.Null;
        public Stream OpenWrite(string path) => Stream.Null;
        public void SetLastWriteTimeUtc(string path, DateTime utc) { }
        public IEnumerable<FileEntry> EnumerateRecursive(string path) => [];
        public VolumeInfo? VolumeFor(string path) => null;
    }
}

public sealed class SearchScopeDirNameTests
{
    [AvaloniaFact]
    public void ScopeDirName_local_subdirectory_returns_leaf()
    {
        var vm = new SearchViewModel(() => "/home/user/projects");
        vm.ScopeDir = "/home/user/projects";

        Assert.Equal("projects", vm.ScopeDirName);
    }

    [AvaloniaFact]
    public void ScopeDirName_remote_subdirectory_returns_leaf()
    {
        var vm = new SearchViewModel(() => "sftp://srv1/sub");
        vm.ScopeDir = "sftp://srv1/sub";

        Assert.Equal("sub", vm.ScopeDirName);
    }

    [AvaloniaFact]
    public void ScopeDirName_remote_root_returns_connection_name()
    {
        var vm = new SearchViewModel(() => "sftp://srv1/");
        vm.ConnectionNameResolver = id => id == "srv1" ? "Production Server" : null;
        vm.ScopeDir = "sftp://srv1/";

        Assert.Equal("Production Server", vm.ScopeDirName);
    }

    [AvaloniaFact]
    public void ScopeDirName_remote_root_falls_back_to_id_when_name_not_found()
    {
        var vm = new SearchViewModel(() => "sftp://unknown-id/");
        vm.ConnectionNameResolver = _ => null;
        vm.ScopeDir = "sftp://unknown-id/";

        Assert.Equal("unknown-id", vm.ScopeDirName);
    }

    [AvaloniaFact]
    public void ScopeDirName_local_root_returns_full_path()
    {
        // On Unix "/" has no leaf → returns the full path.
        var vm = new SearchViewModel(() => "/");
        vm.ScopeDir = "/";

        Assert.Equal("/", vm.ScopeDirName);
    }
}
