using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

// The SMB path of the unified (protocol-aware) ConnectDialogViewModel.
public sealed class SmbConnectDialogTests
{
    private static (ConnectDialogViewModel Vm,
                    List<(SmbConnectionInfo Info, ConnectSecret Secret)> Connects,
                    List<StoredSmbConnection> Saved)
        MakeVm(Action<SmbConnectionInfo, ConnectSecret>? connectOverride = null)
    {
        var connects = new List<(SmbConnectionInfo, ConnectSecret)>();
        var saved = new List<StoredSmbConnection>();

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        var smbStore = new SmbConnectionStore(":mem:", _ => null, (_, _) => { });
        var codec = new SecretCodec();
        var manager = new ConnectionManager(registry, hks);
        var smbManager = new SmbConnectionManager(registry, new FakeSmbFactory(new FakeSmbClientAdapter()));
        var s3Store = new S3ConnectionStore(":mem:", _ => null, (_, _) => { });
        var s3Manager = new S3ConnectionManager(registry);

        var vm = new ConnectDialogViewModel(manager, store, hks, codec, smbManager, smbStore, s3Manager, s3Store)
        {
            Protocol = ConnectProtocol.Smb,
        };
        vm.SmbConnectAction = connectOverride ?? ((i, s) => connects.Add((i, s)));
        vm.SmbSaveAction = s => saved.Add(s);

        return (vm, connects, saved);
    }

    [AvaloniaFact]
    public void Selecting_smb_switches_port_default_and_field_visibility()
    {
        var (vm, _, _) = MakeVm();
        Assert.True(vm.IsSmb);
        Assert.Equal("445", vm.PortText);
        Assert.True(vm.SmbFieldsVisible);
        Assert.False(vm.SftpAuthVisible);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_host_is_empty()
    {
        var (vm, _, saved) = MakeVm();
        vm.Username = "alice";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_username_empty_and_not_guest()
    {
        var (vm, _, saved) = MakeVm();
        vm.Host = "nas.local";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Guest_connects_without_username_and_with_empty_secret()
    {
        var (vm, connects, saved) = MakeVm();
        vm.Host = "nas.local";
        vm.Guest = true;

        await vm.ConnectAsync();

        Assert.False(vm.HasError);
        Assert.Single(connects);
        Assert.True(connects[0].Info.Guest);
        Assert.Equal("", connects[0].Secret.Password);
        Assert.Single(saved);
        Assert.True(saved[0].Guest);
        Assert.Empty(saved[0].ObfuscatedSecret);
    }

    [AvaloniaFact]
    public void Guest_toggle_hides_username_and_password()
    {
        var (vm, _, _) = MakeVm();
        Assert.True(vm.UsernameVisible);
        Assert.True(vm.PasswordVisible);

        vm.Guest = true;
        Assert.False(vm.UsernameVisible);
        Assert.False(vm.PasswordVisible);
    }

    [AvaloniaFact]
    public async Task Successful_connect_saves_and_raises_SmbConnected()
    {
        var (vm, connects, saved) = MakeVm();
        SmbConnectionInfo? connected = null;
        vm.SmbConnected += i => connected = i;

        vm.Host = "nas.local";
        vm.Username = "alice";
        vm.Password = "pw";
        vm.Domain = "WORKGROUP";

        await vm.ConnectAsync();

        Assert.False(vm.HasError);
        Assert.Single(connects);
        Assert.Equal("nas.local", connects[0].Info.Host);
        Assert.Equal("WORKGROUP", connects[0].Info.Domain);
        Assert.Single(saved);
        Assert.NotNull(connected);
    }

    [AvaloniaFact]
    public async Task Authentication_failure_sets_error_and_does_not_save()
    {
        var (vm, _, saved) = MakeVm((_, _) => throw new SmbAuthenticationException("bad creds"));
        vm.Host = "nas.local";
        vm.Username = "alice";
        vm.Password = "wrong";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public void ForEdit_smb_sets_protocol_and_fields()
    {
        var (vm, _, _) = MakeVm();
        var stored = new StoredSmbConnection
        {
            Id = "s1",
            Name = "NAS",
            Host = "nas.local",
            Port = 445,
            Username = "alice",
            Domain = "WORKGROUP",
            Guest = false,
            InitialPath = "/media",
            SavePassword = false,
        };

        vm.ForEdit(stored);

        Assert.Equal(ConnectProtocol.Smb, vm.Protocol);
        Assert.Equal("NAS", vm.Name);
        Assert.Equal("nas.local", vm.Host);
        Assert.Equal("alice", vm.Username);
        Assert.Equal("WORKGROUP", vm.Domain);
        Assert.Equal("/media", vm.InitialRemotePath);
    }
}
