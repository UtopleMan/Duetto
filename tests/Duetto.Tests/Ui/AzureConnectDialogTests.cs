using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

// The Azure Blob path of the unified (protocol-aware) ConnectDialogViewModel.
public sealed class AzureConnectDialogTests
{
    private static (ConnectDialogViewModel Vm,
                    List<(AzureConnectionInfo Info, ConnectSecret Secret)> Connects,
                    List<StoredAzureConnection> Saved)
        MakeVm()
    {
        var connects = new List<(AzureConnectionInfo, ConnectSecret)>();
        var saved = new List<StoredAzureConnection>();

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        var smbStore = new SmbConnectionStore(":mem:", _ => null, (_, _) => { });
        var s3Store = new S3ConnectionStore(":mem:", _ => null, (_, _) => { });
        var azureStore = new AzureConnectionStore(":mem:", _ => null, (_, _) => { });
        var codec = new SecretCodec();
        var manager = new ConnectionManager(registry, hks);
        var smbManager = new SmbConnectionManager(registry);
        var s3Manager = new S3ConnectionManager(registry);
        var azureManager = new AzureConnectionManager(registry);

        var vm = new ConnectDialogViewModel(manager, store, hks, codec, smbManager, smbStore, s3Manager, s3Store, azureManager, azureStore)
        {
            Protocol = ConnectProtocol.AzureBlob,
        };
        vm.AzureConnectAction = (i, s) => connects.Add((i, s));
        vm.AzureSaveAction = s => saved.Add(s);

        return (vm, connects, saved);
    }

    [AvaloniaFact]
    public void Azure_protocol_hides_host_port_and_shows_azure_fields()
    {
        var (vm, _, _) = MakeVm();
        Assert.True(vm.IsAzure);
        Assert.True(vm.AzureFieldsVisible);
        Assert.False(vm.HostPortVisible);
        Assert.False(vm.UsernameVisible);
    }

    [AvaloniaFact]
    public void Each_auth_mode_shows_only_its_own_secret_field()
    {
        var (vm, _, _) = MakeVm();

        vm.AzureAuth = AzureAuthMode.SharedKey;
        Assert.True(vm.AzureKeyVisible);
        Assert.False(vm.AzureConnStringVisible);
        Assert.False(vm.AzureSasVisible);
        Assert.True(vm.AzureAccountVisible);
        Assert.True(vm.SaveSecretVisible);

        vm.AzureAuth = AzureAuthMode.ConnectionString;
        Assert.False(vm.AzureKeyVisible);
        Assert.True(vm.AzureConnStringVisible);
        Assert.False(vm.AzureAccountVisible);
        Assert.True(vm.SaveSecretVisible);

        vm.AzureAuth = AzureAuthMode.Sas;
        Assert.True(vm.AzureSasVisible);
        Assert.False(vm.AzureKeyVisible);
        Assert.True(vm.SaveSecretVisible);

        vm.AzureAuth = AzureAuthMode.Anonymous;
        Assert.False(vm.AzureKeyVisible);
        Assert.False(vm.AzureConnStringVisible);
        Assert.False(vm.AzureSasVisible);
        Assert.False(vm.SaveSecretVisible);
    }

    [AvaloniaFact]
    public async Task SharedKey_auth_requires_account_and_key()
    {
        var (vm, connects, _) = MakeVm();
        vm.AzureAuth = AzureAuthMode.SharedKey;
        vm.AzureAccount = "";
        vm.AzureAccountKey = "k";

        await vm.ConnectAsync();

        Assert.Equal("Storage account name is required", vm.ErrorText);
        Assert.Empty(connects);
    }

    [AvaloniaFact]
    public async Task Anonymous_auth_requires_a_container()
    {
        var (vm, connects, _) = MakeVm();
        vm.AzureAuth = AzureAuthMode.Anonymous;
        vm.AzureAccount = "devstoreaccount1";
        vm.AzureContainer = "";

        await vm.ConnectAsync();

        Assert.Equal("Container is required for anonymous access", vm.ErrorText);
        Assert.Empty(connects);
    }

    [AvaloniaFact]
    public async Task Valid_shared_key_connect_builds_info_and_secret_and_saves()
    {
        var (vm, connects, saved) = MakeVm();
        vm.Name = "Azurite";
        vm.Endpoint = "http://127.0.0.1:10000/devstoreaccount1";
        vm.AzureAuth = AzureAuthMode.SharedKey;
        vm.AzureAccount = "devstoreaccount1";
        vm.AzureAccountKey = "shh";
        vm.AzureContainer = "duetto";
        vm.InitialRemotePath = "/duetto";
        vm.SavePassword = true;

        await vm.ConnectAsync();

        var (info, secret) = Assert.Single(connects);
        Assert.Equal("http://127.0.0.1:10000/devstoreaccount1", info.Endpoint);
        Assert.Equal("devstoreaccount1", info.AccountName);
        Assert.Equal(AzureAuthMode.SharedKey, info.AuthMode);
        Assert.Equal("duetto", info.Container);
        Assert.Equal("/duetto", info.InitialPath);
        Assert.Equal("shh", secret.Password);

        var stored = Assert.Single(saved);
        Assert.NotEmpty(stored.ObfuscatedSecret);
    }

    [AvaloniaFact]
    public async Task Connection_string_connect_carries_the_string_as_the_secret()
    {
        var (vm, connects, _) = MakeVm();
        vm.AzureAuth = AzureAuthMode.ConnectionString;
        vm.AzureConnectionString = "UseDevelopmentStorage=true";
        vm.AzureContainer = "duetto";

        await vm.ConnectAsync();

        var (info, secret) = Assert.Single(connects);
        Assert.Equal(AzureAuthMode.ConnectionString, info.AuthMode);
        Assert.Equal("UseDevelopmentStorage=true", secret.Password);
    }
}
