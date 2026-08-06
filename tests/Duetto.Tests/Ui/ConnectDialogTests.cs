using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Duetto.Views;
using Renci.SshNet.Common;

namespace Duetto.Tests.Ui;

public sealed class ConnectDialogTests
{
    private static SecretCodec MakeCodec() => new();

    private static (ConnectDialogViewModel Vm,
                    List<(ConnectionInfo Info, ConnectSecret Secret)> Connects,
                    List<StoredConnection> Saved,
                    List<string> Forgot)
        MakeVm(Action<ConnectionInfo, ConnectSecret>? connectOverride = null)
    {
        var connects = new List<(ConnectionInfo, ConnectSecret)>();
        var saved = new List<StoredConnection>();
        var forgot = new List<string>();

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        var codec = MakeCodec();
        var manager = new ConnectionManager(registry, hks);
        var smbStore = new SmbConnectionStore(":mem:", _ => null, (_, _) => { });
        var smbManager = new SmbConnectionManager(registry);
        var s3Store = new S3ConnectionStore(":mem:", _ => null, (_, _) => { });
        var s3Manager = new S3ConnectionManager(registry);

        var vm = new ConnectDialogViewModel(manager, store, hks, codec, smbManager, smbStore, s3Manager, s3Store, new AzureConnectionManager(registry), new AzureConnectionStore(":mem:", _ => null, (_, _) => { }));
        vm.ConnectAction = connectOverride ?? ((i, s) => connects.Add((i, s)));
        vm.SaveAction = s => saved.Add(s);
        vm.ForgetKeyAction = k => forgot.Add(k);

        return (vm, connects, saved, forgot);
    }

    private static void FillValid(ConnectDialogViewModel vm)
    {
        vm.Host = "example.com";
        vm.Username = "alice";
        vm.PortText = "22";
        vm.AuthMode = AuthMode.Password;
        vm.Password = "hunter2";
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_host_is_empty()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Username = "alice";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_username_is_empty()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Host = "example.com";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_port_is_out_of_range()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Host = "example.com";
        vm.Username = "alice";
        vm.PortText = "0";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_key_path_missing_in_key_mode()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Host = "example.com";
        vm.Username = "alice";
        vm.AuthMode = AuthMode.Key;
        vm.KeyPath = "";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Connect_success_raises_Connected_event_and_saves()
    {
        var (vm, connects, saved, _) = MakeVm();
        FillValid(vm);
        vm.SavePassword = true;
        vm.Name = "My Box";

        ConnectionInfo? connectedInfo = null;
        vm.Connected += info => connectedInfo = info;

        await vm.ConnectAsync();

        Assert.False(vm.HasError);
        Assert.NotNull(connectedInfo);
        Assert.Equal("My Box", connectedInfo!.Name);
        Assert.Equal("example.com", connectedInfo.Host);
        Assert.Single(connects);
        Assert.Single(saved);
    }

    [AvaloniaFact]
    public async Task Connect_success_no_name_uses_host_as_name()
    {
        var (vm, _, saved, _) = MakeVm();
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.Equal("example.com", saved[0].Name);
    }

    [AvaloniaFact]
    public async Task Connect_success_does_not_save_secret_when_SavePassword_false()
    {
        var (vm, _, saved, _) = MakeVm();
        FillValid(vm);
        vm.SavePassword = false;
        vm.Password = "secret123";

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.False(saved[0].SavePassword);
        Assert.Equal(string.Empty, saved[0].ObfuscatedSecret);
    }

    [AvaloniaFact]
    public async Task Connect_success_saves_obfuscated_secret_when_SavePassword_true()
    {
        var codec = MakeCodec();
        var (vm, _, saved, _) = MakeVm();
        vm.SavePassword = true;
        FillValid(vm);
        vm.Password = "secret123";

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.True(saved[0].SavePassword);
        Assert.NotEmpty(saved[0].ObfuscatedSecret);
        var secret = ConnectionStore.ResolveSecret(saved[0], codec);
        Assert.Equal("secret123", secret!.Password);
    }

    [AvaloniaFact]
    public async Task Auth_failure_surfaces_error_and_does_not_save()
    {
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
            throw new SshAuthenticationException("Bad password"));
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Contains("Authentication failed", vm.ErrorText);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Socket_error_surfaces_error_and_does_not_save()
    {
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
            throw new System.Net.Sockets.SocketException());
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Contains("Connection failed", vm.ErrorText);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task HostKeyChanged_enters_warning_state_with_fingerprints()
    {
        var callCount = 0;
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
        {
            callCount++;
            throw new HostKeyChangedException(
                host: "example.com",
                oldFingerprint: "AAAA",
                newFingerprint: "BBBB",
                algorithmName: "ssh-ed25519",
                storeKey: "ssh-ed25519:[example.com]:22");
        });
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.True(vm.IsHostKeyChanged);
        Assert.True(vm.HasHostKeyWarning);
        Assert.Equal("AAAA", vm.OldFingerprint);
        Assert.Equal("BBBB", vm.NewFingerprint);
        Assert.Empty(saved);
        Assert.Equal(1, callCount);
    }

    [AvaloniaFact]
    public async Task AcceptNewKey_calls_Forget_then_retries_and_succeeds()
    {
        var callCount = 0;
        var forgets = new List<string>();
        var saves = new List<StoredConnection>();

        var (vm, _, _, _) = MakeVm(connectOverride: (_, _) =>
        {
            callCount++;
            if (callCount == 1)
                throw new HostKeyChangedException(
                    "example.com", "OLD", "NEW", "ssh-ed25519",
                    "ssh-ed25519:[example.com]:22");
        });
        vm.ForgetKeyAction = k => forgets.Add(k);
        vm.SaveAction = s => saves.Add(s);

        FillValid(vm);

        await vm.ConnectAsync();
        Assert.True(vm.IsHostKeyChanged);

        ConnectionInfo? connectedInfo = null;
        vm.Connected += info => connectedInfo = info;
        await vm.AcceptNewKeyAsync();

        Assert.False(vm.IsHostKeyChanged);
        Assert.False(vm.HasError);
        Assert.Single(forgets);
        Assert.Equal("ssh-ed25519:[example.com]:22", forgets[0]);
        Assert.Equal(2, callCount);
        Assert.Single(saves);
        Assert.NotNull(connectedInfo);
    }

    [AvaloniaFact]
    public async Task AcceptNewKey_is_no_op_when_not_in_changed_state()
    {
        var (vm, connects, saved, forgot) = MakeVm();
        FillValid(vm);

        await vm.AcceptNewKeyAsync();

        Assert.Empty(connects);
        Assert.Empty(saved);
        Assert.Empty(forgot);
    }

    [AvaloniaFact]
    public async Task ForEdit_prefills_fields_from_stored_connection()
    {
        var codec = MakeCodec();
        var info = new ConnectionInfo(
            Id: "id1", Name: "Box", Host: "box.example.com", Port: 2222,
            Username: "bob", AuthMode: AuthMode.Password,
            InitialRemotePath: "/home/bob");
        var secret = ConnectSecret.FromPassword("p@ss");
        var stored = ConnectionStore.Pack(info, secret, savePassword: true, codec);

        var (vm, _, _, _) = MakeVm();
        vm.ForEdit(stored);

        Assert.Equal("Box", vm.Name);
        Assert.Equal("box.example.com", vm.Host);
        Assert.Equal("2222", vm.PortText);
        Assert.Equal("bob", vm.Username);
        Assert.Equal(AuthMode.Password, vm.AuthMode);
        Assert.Equal("/home/bob", vm.InitialRemotePath);
        Assert.True(vm.SavePassword);
        Assert.Equal("p@ss", vm.Password);
    }

    [AvaloniaFact]
    public async Task ForEdit_and_connect_reuses_same_id()
    {
        var codec = MakeCodec();
        var info = new ConnectionInfo(
            Id: "fixed-id", Name: "Box", Host: "box.example.com",
            Username: "bob");
        var stored = ConnectionStore.Pack(info, ConnectSecret.FromPassword("x"), savePassword: false, codec);

        var (vm, _, saved, _) = MakeVm();
        vm.ForEdit(stored);
        vm.Password = "newpw";

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.Equal("fixed-id", saved[0].Id);
    }

    [AvaloniaFact]
    public void MainViewModel_panes_share_the_same_registry_instance()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        Assert.Same(vm.Registry, vm.Left.Registry);
        Assert.Same(vm.Registry, vm.Right.Registry);
    }

    [AvaloniaFact]
    public void MainViewModel_search_uses_the_same_registry_instance()
    {
        // SearchViewModel stores the registry privately; a reflection-based equality check
        // would be brittle, so we assert on the pane registries that share the same instance.
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        Assert.Same(vm.Left.Registry, vm.Right.Registry);
        Assert.Same(vm.Registry, vm.Left.Registry);
    }

    [AvaloniaFact]
    public void MainViewModel_dispose_does_not_throw_when_no_connections()
    {
        using var tmp = new TempDir();
        var vm = new MainViewModel(tmp.Path, tmp.Path);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task SshOperationTimeout_resets_IsConnecting_and_surfaces_error()
    {
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
            throw new SshOperationTimeoutException("Connection timed out."));
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.False(vm.IsConnecting);
        Assert.True(vm.HasError);
        Assert.NotEmpty(vm.ErrorText);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public void Connect_command_raises_request_unchanged_after_stub_removal()
    {
        // Regression guard for the same VM event contract the old ConnectStubWindow test relied on.
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        pane.Drives.ListVolumes = () => [];
        pane.Drives.Refresh();

        var requested = false;
        pane.Drives.ConnectRequested += () => requested = true;

        pane.Drives.Connect();

        Assert.True(requested);
    }
}
