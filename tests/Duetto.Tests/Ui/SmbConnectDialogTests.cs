using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public sealed class SmbConnectDialogTests
{
    private static (SmbConnectDialogViewModel Vm,
                    List<(SmbConnectionInfo Info, ConnectSecret Secret)> Connects,
                    List<StoredSmbConnection> Saved)
        MakeVm(Action<SmbConnectionInfo, ConnectSecret>? connectOverride = null)
    {
        var connects = new List<(SmbConnectionInfo, ConnectSecret)>();
        var saved = new List<StoredSmbConnection>();

        var registry = new FileSystemRegistry();
        var store = new SmbConnectionStore(":mem:", _ => null, (_, _) => { });
        var codec = new SecretCodec();
        var manager = new SmbConnectionManager(registry, new FakeSmbFactory(new FakeSmbClientAdapter()));

        var vm = new SmbConnectDialogViewModel(manager, store, codec);
        vm.ConnectAction = connectOverride ?? ((i, s) => connects.Add((i, s)));
        vm.SaveAction = s => saved.Add(s);

        return (vm, connects, saved);
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
        // Guest never persists a secret even if SavePassword happened to be set.
        Assert.Empty(saved[0].ObfuscatedSecret);
    }

    [AvaloniaFact]
    public void Guest_toggle_hides_credentials()
    {
        var (vm, _, _) = MakeVm();
        Assert.True(vm.CredentialsVisible);

        vm.Guest = true;
        Assert.False(vm.CredentialsVisible);
    }

    [AvaloniaFact]
    public async Task Successful_connect_saves_and_raises_Connected()
    {
        var (vm, connects, saved) = MakeVm();
        SmbConnectionInfo? connected = null;
        vm.Connected += i => connected = i;

        vm.Host = "nas.local";
        vm.Username = "alice";
        vm.Password = "pw";
        vm.Domain = "WORKGROUP";
        vm.PortText = "445";

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
    public void ForEdit_populates_fields_from_stored()
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

        Assert.Equal("NAS", vm.Name);
        Assert.Equal("nas.local", vm.Host);
        Assert.Equal("alice", vm.Username);
        Assert.Equal("WORKGROUP", vm.Domain);
        Assert.Equal("/media", vm.InitialPath);
    }
}
