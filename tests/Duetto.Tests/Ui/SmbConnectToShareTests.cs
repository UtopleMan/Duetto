using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public sealed class SmbConnectToShareTests
{
    private static StoredSmbConnection MakeStored(
        string id = "srv1",
        string name = "My NAS",
        string host = "fake.local",
        string initialPath = "/media",
        bool guest = false,
        bool savePassword = false,
        string obfuscated = "") =>
        new()
        {
            Id = id,
            Name = name,
            Host = host,
            Port = 445,
            Username = guest ? "" : "alice",
            Domain = "WORKGROUP",
            Guest = guest,
            InitialPath = initialPath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };

    private sealed class FixedSmbFactory(FakeSmbClientAdapter adapter) : ISmbClientFactory
    {
        public ISmbClientAdapter Create(SmbConnectionInfo info, ConnectSecret secret) => adapter;
    }

    private static string StoreJson(StoredSmbConnection stored) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { stored });

    [AvaloniaFact]
    public void Already_connected_navigates_immediately_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter();
        adapter.CreateDirectory("/media");

        var manager = new SmbConnectionManager(registry, new FixedSmbFactory(adapter));
        manager.Connect(new SmbConnectionInfo("srv1", "My NAS", "fake.local"), ConnectSecret.FromPassword("pw"));

        var store = new SmbConnectionStore(":mem:", _ => null, (_, _) => { });
        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, smbConnectionManager: manager, smbConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredSmbConnection?>();
        vm.OpenSmbConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToSmbShare(MakeStored("srv1", initialPath: "/media"), vm.Left);

        Assert.Equal("smb://srv1/media", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_succeeds_navigates_and_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter();
        adapter.CreateDirectory("/media");

        var manager = new SmbConnectionManager(registry, new FixedSmbFactory(adapter));
        var codec = new SecretCodec();
        var stored = MakeStored("srv1", initialPath: "/media", savePassword: true, obfuscated: codec.Encrypt("pw"));
        var store = new SmbConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, smbConnectionManager: manager, smbConnectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredSmbConnection?>();
        vm.OpenSmbConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToSmbShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("smb://srv1/media", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Guest_connects_without_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter();
        adapter.CreateDirectory("/public");

        var manager = new SmbConnectionManager(registry, new FixedSmbFactory(adapter));
        var stored = MakeStored("srv1", initialPath: "/public", guest: true);
        var store = new SmbConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, smbConnectionManager: manager, smbConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredSmbConnection?>();
        vm.OpenSmbConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToSmbShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("smb://srv1/public", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_auth_failure_opens_dialog_no_navigate()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter { NextConnectThrow = new SmbAuthenticationException("bad") };

        var manager = new SmbConnectionManager(registry, new FixedSmbFactory(adapter));
        var codec = new SecretCodec();
        var stored = MakeStored("srv1", savePassword: true, obfuscated: codec.Encrypt("bad"));
        var store = new SmbConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, smbConnectionManager: manager, smbConnectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredSmbConnection?>();
        vm.OpenSmbConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        var before = vm.Left.CurrentPath;
        vm.ConnectToSmbShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(dialogsOpened);
        Assert.Equal(before, vm.Left.CurrentPath);
    }

    [AvaloniaFact]
    public void No_secret_opens_dialog_prefilled()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter();
        var manager = new SmbConnectionManager(registry, new FixedSmbFactory(adapter));

        var stored = MakeStored("srv1", savePassword: false);
        var store = new SmbConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, smbConnectionManager: manager, smbConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredSmbConnection?>();
        vm.OpenSmbConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToSmbShare(stored, vm.Left);

        Assert.Single(dialogsOpened);
        Assert.Equal("srv1", dialogsOpened[0]!.Id);
    }
}

public sealed class SmbSharesPopoverMergeTests
{
    private static StoredConnection Sftp(string id, string name) => new()
    {
        Id = id, Name = name, Host = "ssh.example.com", Port = 22, Username = "u",
        AuthMode = AuthMode.Password, InitialRemotePath = "/",
    };

    private static StoredSmbConnection Smb(string id, string name) => new()
    {
        Id = id, Name = name, Host = "nas.local", Port = 445, Username = "u", InitialPath = "/",
    };

    [AvaloniaFact]
    public void Popover_merges_sftp_and_smb_rows_tagged_by_scheme()
    {
        using var pane = new PaneViewModel("/tmp", new FileSystemRegistry());
        var popover = new DrivePopoverViewModel(pane)
        {
            ListConnections = () => [Sftp("a", "SFTP One")],
            ListSmbConnections = () => [Smb("b", "SMB One")],
        };

        popover.Refresh();

        Assert.Equal(2, popover.Shares.Count);
        Assert.Contains(popover.Shares, s => s is { Scheme: "sftp", Name: "SFTP One" });
        Assert.Contains(popover.Shares, s => s is { Scheme: "smb", Name: "SMB One", IsSmb: true });
    }

    [AvaloniaFact]
    public void EditShare_routes_smb_rows_to_the_smb_event()
    {
        using var pane = new PaneViewModel("/tmp", new FileSystemRegistry());
        var popover = new DrivePopoverViewModel(pane)
        {
            ListSmbConnections = () => [Smb("b", "SMB One")],
        };
        popover.Refresh();

        StoredSmbConnection? edited = null;
        StoredConnection? sftpEdited = null;
        popover.EditSmbShareRequested += s => edited = s;
        popover.EditShareRequested += s => sftpEdited = s;

        popover.EditShare(popover.Shares.Single());

        Assert.NotNull(edited);
        Assert.Equal("b", edited.Id);
        Assert.Null(sftpEdited);
    }

    [AvaloniaFact]
    public void ConnectSmb_command_raises_ConnectSmbRequested()
    {
        using var pane = new PaneViewModel("/tmp", new FileSystemRegistry());
        var popover = new DrivePopoverViewModel(pane);

        var requested = false;
        popover.ConnectSmbRequested += () => requested = true;

        popover.ConnectSmbCommand.Execute(null);

        Assert.True(requested);
    }
}
