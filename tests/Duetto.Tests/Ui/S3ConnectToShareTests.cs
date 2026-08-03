using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public sealed class S3ConnectToShareTests
{
    private static StoredS3Connection MakeStored(
        string id = "srv1",
        string bucket = "duetto",
        string initialPath = "/duetto",
        S3AuthMode auth = S3AuthMode.Keys,
        bool savePassword = false,
        string obfuscated = "") =>
        new()
        {
            Id = id,
            Name = "My S3",
            Endpoint = "http://127.0.0.1:9000",
            Region = "us-east-1",
            PathStyle = true,
            AuthMode = auth,
            AccessKeyId = auth == S3AuthMode.Keys ? "AKIA" : "",
            Bucket = bucket,
            InitialPath = initialPath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };

    private static string StoreJson(StoredS3Connection stored) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { stored });

    private static FakeS3ClientAdapter SeededAdapter()
    {
        var adapter = new FakeS3ClientAdapter("duetto");
        adapter.Seed("duetto", "readme.txt", [1]);
        return adapter;
    }

    [AvaloniaFact]
    public void Already_connected_navigates_immediately_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        var manager = new S3ConnectionManager(registry, new FakeS3ClientFactory(adapter));
        manager.Connect(new S3ConnectionInfo("srv1", "My S3", Bucket: "duetto"), new ConnectSecret());

        var store = new S3ConnectionStore(":mem:", _ => null, (_, _) => { });
        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, s3ConnectionManager: manager, s3ConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredS3Connection?>();
        vm.OpenS3ConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToS3Share(MakeStored("srv1", initialPath: "/duetto"), vm.Left);

        Assert.Equal("s3://srv1/duetto", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_with_saved_keys_navigates_and_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        var manager = new S3ConnectionManager(registry, new FakeS3ClientFactory(adapter));
        var codec = new SecretCodec();

        var info = new S3ConnectionInfo("srv1", "My S3", Endpoint: "http://127.0.0.1:9000",
            PathStyle: true, AuthMode: S3AuthMode.Keys, AccessKeyId: "AKIA", Bucket: "duetto", InitialPath: "/duetto");
        var stored = S3ConnectionStore.Pack(info, ConnectSecret.FromKeys("shh"), savePassword: true, codec);
        var store = new S3ConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, s3ConnectionManager: manager, s3ConnectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredS3Connection?>();
        vm.OpenS3ConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToS3Share(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("s3://srv1/duetto", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Anonymous_connects_without_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        var manager = new S3ConnectionManager(registry, new FakeS3ClientFactory(adapter));

        var stored = MakeStored("srv1", auth: S3AuthMode.Anonymous);
        var store = new S3ConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, s3ConnectionManager: manager, s3ConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredS3Connection?>();
        vm.OpenS3ConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToS3Share(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("s3://srv1/duetto", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_auth_failure_opens_dialog_no_navigate()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        adapter.NextConnectThrow = new S3AuthenticationException("bad");
        var manager = new S3ConnectionManager(registry, new FakeS3ClientFactory(adapter));
        var codec = new SecretCodec();

        var info = new S3ConnectionInfo("srv1", "My S3", AuthMode: S3AuthMode.Keys, AccessKeyId: "AKIA", Bucket: "duetto", InitialPath: "/duetto");
        var stored = S3ConnectionStore.Pack(info, ConnectSecret.FromKeys("bad"), savePassword: true, codec);
        var store = new S3ConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, s3ConnectionManager: manager, s3ConnectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredS3Connection?>();
        vm.OpenS3ConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        var before = vm.Left.CurrentPath;
        vm.ConnectToS3Share(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(dialogsOpened);
        Assert.Equal(before, vm.Left.CurrentPath);
    }

    [AvaloniaFact]
    public void No_secret_opens_dialog_prefilled()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var manager = new S3ConnectionManager(registry, new FakeS3ClientFactory(SeededAdapter()));

        var stored = MakeStored("srv1", auth: S3AuthMode.Keys, savePassword: false);
        var store = new S3ConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, s3ConnectionManager: manager, s3ConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredS3Connection?>();
        vm.OpenS3ConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToS3Share(stored, vm.Left);

        Assert.Single(dialogsOpened);
        Assert.Equal("srv1", dialogsOpened[0]!.Id);
    }
}

public sealed class S3SharesPopoverMergeTests
{
    private static StoredS3Connection S3(string id, string name) => new()
    {
        Id = id, Name = name, Endpoint = "http://minio:9000", AuthMode = S3AuthMode.Keys, InitialPath = "/",
    };

    [AvaloniaFact]
    public void Popover_merges_s3_rows_tagged_by_scheme()
    {
        using var pane = new PaneViewModel("/tmp", new FileSystemRegistry());
        var popover = new DrivePopoverViewModel(pane)
        {
            ListS3Connections = () => [S3("c", "S3 One")],
        };

        popover.Refresh();

        var row = Assert.Single(popover.Shares);
        Assert.Equal("s3", row.Scheme);
        Assert.True(row.IsS3);
        Assert.Equal("S3", row.SchemeLabel);
    }

    [AvaloniaFact]
    public void EditShare_routes_s3_rows_to_the_s3_event()
    {
        using var pane = new PaneViewModel("/tmp", new FileSystemRegistry());
        var popover = new DrivePopoverViewModel(pane)
        {
            ListS3Connections = () => [S3("c", "S3 One")],
        };
        popover.Refresh();

        StoredS3Connection? edited = null;
        popover.EditS3ShareRequested += s => edited = s;

        popover.EditShare(popover.Shares.Single());

        Assert.NotNull(edited);
        Assert.Equal("c", edited.Id);
    }
}
