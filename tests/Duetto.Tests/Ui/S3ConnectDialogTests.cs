using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public sealed class S3ConnectDialogTests
{
    private static (ConnectDialogViewModel Vm,
                    List<(S3ConnectionInfo Info, ConnectSecret Secret)> Connects,
                    List<StoredS3Connection> Saved)
        MakeVm()
    {
        var connects = new List<(S3ConnectionInfo, ConnectSecret)>();
        var saved = new List<StoredS3Connection>();

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        var smbStore = new SmbConnectionStore(":mem:", _ => null, (_, _) => { });
        var s3Store = new S3ConnectionStore(":mem:", _ => null, (_, _) => { });
        var codec = new SecretCodec();
        var manager = new ConnectionManager(registry, hks);
        var smbManager = new SmbConnectionManager(registry);
        var s3Manager = new S3ConnectionManager(registry);

        var vm = new ConnectDialogViewModel(manager, store, hks, codec, smbManager, smbStore, s3Manager, s3Store, new AzureConnectionManager(registry), new AzureConnectionStore(":mem:", _ => null, (_, _) => { }))
        {
            Protocol = ConnectProtocol.S3,
        };
        vm.S3ConnectAction = (i, s) => connects.Add((i, s));
        vm.S3SaveAction = s => saved.Add(s);

        return (vm, connects, saved);
    }

    [AvaloniaFact]
    public void S3_protocol_hides_host_port_and_shows_s3_fields()
    {
        var (vm, _, _) = MakeVm();
        Assert.True(vm.IsS3);
        Assert.True(vm.S3FieldsVisible);
        Assert.False(vm.HostPortVisible);
        Assert.False(vm.UsernameVisible);
    }

    [AvaloniaFact]
    public void Keys_mode_shows_key_fields_profile_and_anonymous_hide_them()
    {
        var (vm, _, _) = MakeVm();

        vm.S3Auth = S3AuthMode.Keys;
        Assert.True(vm.S3KeysVisible);
        Assert.False(vm.S3ProfileVisible);
        Assert.True(vm.SaveSecretVisible);

        vm.S3Auth = S3AuthMode.Profile;
        Assert.False(vm.S3KeysVisible);
        Assert.True(vm.S3ProfileVisible);
        Assert.False(vm.SaveSecretVisible);

        vm.S3Auth = S3AuthMode.Anonymous;
        Assert.False(vm.S3KeysVisible);
        Assert.False(vm.S3ProfileVisible);
    }

    [AvaloniaFact]
    public async Task Keys_auth_requires_access_key_and_secret()
    {
        var (vm, connects, _) = MakeVm();
        vm.S3Auth = S3AuthMode.Keys;
        vm.AccessKeyId = "";
        vm.SecretKey = "s";

        await vm.ConnectAsync();

        Assert.Equal("Access key ID is required", vm.ErrorText);
        Assert.Empty(connects);
    }

    [AvaloniaFact]
    public async Task Anonymous_auth_requires_a_bucket()
    {
        var (vm, connects, _) = MakeVm();
        vm.S3Auth = S3AuthMode.Anonymous;
        vm.Bucket = "";

        await vm.ConnectAsync();

        Assert.Equal("Bucket is required for anonymous access", vm.ErrorText);
        Assert.Empty(connects);
    }

    [AvaloniaFact]
    public async Task Valid_keys_connect_builds_info_and_secret_and_saves()
    {
        var (vm, connects, saved) = MakeVm();
        vm.Name = "MinIO";
        vm.Endpoint = "http://127.0.0.1:9000";
        vm.Region = "us-east-1";
        vm.PathStyle = true;
        vm.S3Auth = S3AuthMode.Keys;
        vm.AccessKeyId = "AKIA";
        vm.SecretKey = "shh";
        vm.SessionToken = "tok";
        vm.Bucket = "duetto";
        vm.InitialRemotePath = "/duetto";
        vm.SavePassword = true;

        await vm.ConnectAsync();

        var (info, secret) = Assert.Single(connects);
        Assert.Equal("http://127.0.0.1:9000", info.Endpoint);
        Assert.Equal("us-east-1", info.Region);
        Assert.True(info.PathStyle);
        Assert.Equal(S3AuthMode.Keys, info.AuthMode);
        Assert.Equal("AKIA", info.AccessKeyId);
        Assert.Equal("duetto", info.Bucket);
        Assert.Equal("/duetto", info.InitialPath);
        Assert.Equal("shh", secret.Password);
        Assert.Equal("tok", secret.SessionToken);

        var stored = Assert.Single(saved);
        Assert.NotEmpty(stored.ObfuscatedSecret);
    }

    [AvaloniaFact]
    public async Task Anonymous_connect_saves_no_secret()
    {
        var (vm, connects, saved) = MakeVm();
        vm.S3Auth = S3AuthMode.Anonymous;
        vm.Bucket = "public-bucket";
        vm.SavePassword = true;

        await vm.ConnectAsync();

        Assert.Single(connects);
        Assert.Empty(Assert.Single(saved).ObfuscatedSecret);
    }
}
