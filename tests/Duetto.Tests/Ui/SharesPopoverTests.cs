using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;
using Duetto.Views;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Ui;

/// <summary>
/// VM-level and headless UI tests for the CONNECTED SHARES section of
/// <see cref="DrivePopoverViewModel"/> (Task J, Phase 4 part 2).
/// </summary>
public sealed class SharesPopoverTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Builds a popover with the given stored connections injected via the seam.
    /// <paramref name="connectedIds"/> controls which ids IsConnected returns true for.
    /// </summary>
    private static DrivePopoverViewModel Popover(
        PaneViewModel pane,
        StoredConnection[] stored,
        string[]? connectedIds = null)
    {
        var popover = pane.Drives;
        var connected = new HashSet<string>(connectedIds ?? [], StringComparer.OrdinalIgnoreCase);
        popover.ListVolumes = () => [];
        popover.ListConnections = () => stored;
        popover.IsConnected = id => connected.Contains(id);
        popover.Refresh();
        return popover;
    }

    // ── Shares list ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Shares_list_populates_from_store_seam()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var stored = new[] { MakeStored("a", "Alpha"), MakeStored("b", "Beta") };
        var popover = Popover(pane, stored);

        Assert.Equal(2, popover.Shares.Count);
        Assert.Equal("Alpha", popover.Shares[0].Name);
        Assert.Equal("Beta", popover.Shares[1].Name);
        Assert.True(popover.SharesSectionVisible);
    }

    [AvaloniaFact]
    public void Shares_section_hidden_when_no_saved_connections()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var popover = Popover(pane, []);

        Assert.Empty(popover.Shares);
        Assert.False(popover.SharesSectionVisible);
    }

    [AvaloniaFact]
    public void Share_row_reflects_connected_state()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var stored = new[] { MakeStored("live"), MakeStored("dead") };
        var popover = Popover(pane, stored, connectedIds: ["live"]);

        var liveRow = popover.Shares.Single(s => s.Id == "live");
        var deadRow = popover.Shares.Single(s => s.Id == "dead");

        Assert.True(liveRow.IsConnected);
        Assert.False(deadRow.IsConnected);
        Assert.Equal("#2f8f5b", liveRow.DotColor);
        Assert.Equal("#c2bfb5", deadRow.DotColor);
        Assert.Equal("", liveRow.StatusText);
        Assert.Equal("Offline", deadRow.StatusText);
        Assert.False(liveRow.StatusTextVisible);
        Assert.True(deadRow.StatusTextVisible);
    }

    // ── Click-connected: navigate ─────────────────────────────────────────────

    [AvaloniaFact]
    public void Activate_connected_share_raises_ShareActivated()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var stored = new[] { MakeStored("srv1", "Server 1", "srv.example.com", "/home/user") };
        var popover = Popover(pane, stored, connectedIds: ["srv1"]);

        ShareRowViewModel? activated = null;
        popover.ShareActivated += s => activated = s;

        popover.ActivateShare(popover.Shares.Single());

        Assert.NotNull(activated);
        Assert.Equal("srv1", activated!.Id);
        Assert.Equal("/home/user", activated.InitialRemotePath);
    }

    // ── Click-not-connected-no-secret: raises EditShareRequested ─────────────

    [AvaloniaFact]
    public void Activate_offline_share_raises_ShareActivated_for_connect_or_prompt()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var stored = new[] { MakeStored("srv1") };
        var popover = Popover(pane, stored, connectedIds: []); // not connected

        ShareRowViewModel? activated = null;
        popover.ShareActivated += s => activated = s;

        popover.ActivateShare(popover.Shares.Single());

        // The VM fires ShareActivated regardless; PaneView decides what to do.
        Assert.NotNull(activated);
    }

    // ── Edit affordance ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void EditShare_raises_EditShareRequested_with_stored_connection()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var stored = new[] { MakeStored("edit-me", "Edit Target") };
        var popover = Popover(pane, stored);

        StoredConnection? requested = null;
        popover.EditShareRequested += s => requested = s;

        popover.EditShare(popover.Shares.Single());

        Assert.NotNull(requested);
        Assert.Equal("edit-me", requested!.Id);
        Assert.Equal("Edit Target", requested.Name);
    }

    // ── Remove affordance ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void RemoveShare_raises_RemoveShareRequested_with_connection_id()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var stored = new[] { MakeStored("rm-me") };
        var popover = Popover(pane, stored);

        string? removedId = null;
        popover.RemoveShareRequested += id => removedId = id;

        popover.RemoveShare(popover.Shares.Single());

        Assert.Equal("rm-me", removedId);
    }

    // ── Disconnect row ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Disconnect_row_hidden_when_pane_is_local()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var popover = Popover(pane, []);

        Assert.False(popover.DisconnectRowVisible);
    }

    [AvaloniaFact]
    public void Disconnect_row_visible_when_pane_is_remote()
    {
        using var tmp = new TempDir();
        // Construct pane with a fake registry so NavigateTo("sftp://...") succeeds.
        var registry = new FileSystemRegistry();
        var fakeProvider = new FakeProvider();
        registry.Register("sftp", "srv1", fakeProvider);
        using var pane = new PaneViewModel("sftp://srv1/", registry);
        pane.Lister = _ => [];

        var stored = new[] { MakeStored("srv1", "My SFTP") };
        var popover = pane.Drives;
        popover.ListVolumes = () => [];
        popover.ListConnections = () => stored;
        popover.IsConnected = _ => true;
        popover.Refresh();

        Assert.True(popover.DisconnectRowVisible);
    }

    [AvaloniaFact]
    public void Disconnect_label_uses_connection_name()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv1", new FakeProvider());
        using var pane = new PaneViewModel("sftp://srv1/", registry);
        pane.Lister = _ => [];

        var stored = new[] { MakeStored("srv1", "My SFTP") };
        var popover = pane.Drives;
        popover.ListVolumes = () => [];
        popover.ListConnections = () => stored;
        popover.IsConnected = _ => true;
        popover.Refresh();

        Assert.Equal("Disconnect My SFTP", popover.DisconnectLabel);
    }

    [AvaloniaFact]
    public void Disconnect_command_raises_DisconnectRequested()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv1", new FakeProvider());
        using var pane = new PaneViewModel("sftp://srv1/", registry);
        pane.Lister = _ => [];

        var stored = new[] { MakeStored("srv1", "My SFTP") };
        var popover = pane.Drives;
        popover.ListVolumes = () => [];
        popover.ListConnections = () => stored;
        popover.IsConnected = _ => true;
        popover.Refresh();

        var disconnected = false;
        popover.DisconnectRequested += () => disconnected = true;

        popover.Disconnect();

        Assert.True(disconnected);
    }

    // ── Volume chip shows connection name for remote path ─────────────────────

    [AvaloniaFact]
    public void Chip_shows_connection_name_for_remote_path()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv1", new FakeProvider());
        using var pane = new PaneViewModel("sftp://srv1/home/user", registry);
        pane.Lister = _ => [];

        // Wire the seam so the chip can look up the connection name.
        pane.Drives.ListConnections = () => [MakeStored("srv1", "Production Server")];
        pane.Drives.ListVolumes = () => [];

        Assert.Equal("Production Server", pane.VolumeChipText);
    }

    [AvaloniaFact]
    public void Chip_falls_back_to_id_when_connection_name_not_in_store()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "unknown-id", new FakeProvider());
        using var pane = new PaneViewModel("sftp://unknown-id/", registry);
        pane.Lister = _ => [];

        pane.Drives.ListConnections = () => [];
        pane.Drives.ListVolumes = () => [];

        Assert.Equal("unknown-id", pane.VolumeChipText);
    }

    [AvaloniaFact]
    public void PathTailText_is_local_path_for_remote_address()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv1", new FakeProvider());
        using var pane = new PaneViewModel("sftp://srv1/home/user", registry);
        pane.Lister = _ => [];
        pane.Drives.ListVolumes = () => [];
        pane.Drives.ListConnections = () => [];

        Assert.Equal("/home/user", pane.PathTailText);
    }

    [AvaloniaFact]
    public void PathTailText_is_empty_at_remote_root()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv1", new FakeProvider());
        using var pane = new PaneViewModel("sftp://srv1/", registry);
        pane.Lister = _ => [];
        pane.Drives.ListVolumes = () => [];
        pane.Drives.ListConnections = () => [];

        Assert.Equal("", pane.PathTailText);
    }

    // ── Headless UI: shares section visibility ────────────────────────────────

    [AvaloniaFact]
    public void Popover_shows_shares_section_when_connections_exist()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        // Wire the left pane's popover to return one connection.
        vm.Left.Drives.ListConnections = () => [MakeStored("srv1", "Test Server")];
        vm.Left.Drives.ListVolumes = () => [];
        vm.Left.Drives.Refresh();

        var window = new MainWindow(vm);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The shares section should be visible when there are connections.
        Assert.True(vm.Left.Drives.SharesSectionVisible);
        Assert.Single(vm.Left.Drives.Shares);
        Assert.Equal("Test Server", vm.Left.Drives.Shares[0].Name);

        window.Close();
    }

    [AvaloniaFact]
    public void Popover_hides_shares_section_when_no_connections()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        vm.Left.Drives.ListConnections = () => [];
        vm.Left.Drives.ListVolumes = () => [];
        vm.Left.Drives.Refresh();

        var window = new MainWindow(vm);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.Left.Drives.SharesSectionVisible);
        Assert.Empty(vm.Left.Drives.Shares);

        window.Close();
    }

    // ── Remote places (GNOME rail) ────────────────────────────────────────────

    [AvaloniaFact]
    public void RebuildRemotePlaces_populates_from_connection_store()
    {
        using var tmp = new TempDir();
        var stored = new[] { MakeStored("s1", "Server A"), MakeStored("s2", "Server B") };
        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        // Inject the store by overriding the reader.
        var codec = new SecretCodec();
        var json = System.Text.Json.JsonSerializer.Serialize(stored);
        var store2 = new ConnectionStore(":mem:", _ => json, (_, _) => { });
        using var vm = new MainViewModel(tmp.Path, tmp.Path, connectionStore: store2);

        vm.RebuildRemotePlaces();

        Assert.Equal(2, vm.RemotePlaces.Count);
        Assert.Equal("Server A", vm.RemotePlaces[0].Name);
        Assert.Equal("Server B", vm.RemotePlaces[1].Name);
        Assert.True(vm.RemotePlacesVisible);
    }

    [AvaloniaFact]
    public void RebuildRemotePlaces_empty_when_no_connections()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        // Default store has no-op reader → empty list.
        vm.RebuildRemotePlaces();

        Assert.Empty(vm.RemotePlaces);
        Assert.False(vm.RemotePlacesVisible);
    }

    // ── Remove disconnects a live share ───────────────────────────────────────

    /// <summary>Factory returning one fixed fake adapter (mirrors ConnectionManagerTests).</summary>
    private sealed class FixedAdapterFactory(FakeSftpClientAdapter adapter) : ISftpClientFactory
    {
        public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => adapter;
    }

    [AvaloniaFact]
    public void Removing_connected_share_disconnects_and_navigates_pane_home()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var manager = new ConnectionManager(registry, hks, new FixedAdapterFactory(new FakeSftpClientAdapter()));

        var storedJson = System.Text.Json.JsonSerializer.Serialize(new[] { MakeStored("srv1", "My Server") });
        var store = new ConnectionStore(":mem:", _ => storedJson, (_, content) => storedJson = content);

        using var vm = new MainViewModel(
            tmp.Path, tmp.Path,
            registry: registry, connectionManager: manager,
            connectionStore: store, hostKeyStore: hks);

        // Establish the live connection and put the left pane on it.
        manager.Connect(new DuettoConnectionInfo("srv1", "My Server", "fake.local"), ConnectSecret.FromPassword("pw"));
        vm.Left.NavigateTo("sftp://srv1/");
        Assert.True(manager.IsConnected("srv1"));
        Assert.Equal("sftp://srv1/", vm.Left.CurrentPath);

        var window = new MainWindow(vm);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Fire the remove affordance for the share (PaneView handles RemoveShareRequested).
        var share = new ShareRowViewModel(MakeStored("srv1", "My Server"), isConnected: true);
        vm.Left.Drives.RemoveShare(share);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The live session is disconnected, the pane left the share, and the record is gone.
        Assert.False(manager.IsConnected("srv1"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), vm.Left.CurrentPath);
        Assert.Empty(store.Load());
        Assert.Empty(vm.RemotePlaces);

        window.Close();
    }

    // ── Remote path: no volume/capacity in popover ────────────────────────────

    [AvaloniaFact]
    public void Remote_path_has_no_current_volume_or_capacity_in_popover()
    {
        // A pane at a remote (sftp://) path opens the drive popover:
        // Current must be null (no local volume), CanEject false (no eject row),
        // and the eject row is hidden — the popover makes no capacity claims.
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv1", new FakeProvider());
        using var pane = new PaneViewModel("sftp://srv1/home/user", registry);
        pane.Lister = _ => [];

        var popover = pane.Drives;
        popover.ListVolumes = () => [];
        popover.ListConnections = () => [MakeStored("srv1", "My SFTP")];
        popover.IsConnected = _ => true;
        popover.Refresh();

        Assert.Null(popover.Current);
        Assert.False(popover.CanEject);
        Assert.False(popover.EjectRowVisible);
    }
}

/// <summary>
/// Minimal <see cref="IFileSystemProvider"/> stub for constructing test panes
/// with a remote path without triggering any real filesystem calls.
/// </summary>
internal sealed class FakeProvider : IFileSystemProvider
{
    public FileSystemCapabilities Capabilities { get; } = new()
    {
        CanRename = false, CanCreateEmptyDir = false, CanCreateFile = false,
        CanDelete = false, HasTrash = false, HasPermissions = false,
        PreservesMTime = false, AtomicRename = false, CanWatch = false,
        ReportsCapacity = false, SupportsSearch = false, CaseSensitive = true,
        Separator = '/',
    };

    public IReadOnlyList<FileEntry> List(string path) => [];
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
