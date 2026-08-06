using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public sealed class AzureConnectToShareTests
{
    private static StoredAzureConnection MakeStored(
        string id = "srv1",
        string container = "duetto",
        string initialPath = "/duetto",
        AzureAuthMode auth = AzureAuthMode.SharedKey,
        bool savePassword = false,
        string obfuscated = "") =>
        new()
        {
            Id = id,
            Name = "My Azure",
            Endpoint = "http://127.0.0.1:10000/devstoreaccount1",
            AccountName = "devstoreaccount1",
            AuthMode = auth,
            Container = container,
            InitialPath = initialPath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };

    private static string StoreJson(StoredAzureConnection stored) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { stored });

    private static FakeAzureClientAdapter SeededAdapter()
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        adapter.Seed("duetto", "readme.txt", [1]);
        return adapter;
    }

    [AvaloniaFact]
    public void Already_connected_navigates_immediately_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        var manager = new AzureConnectionManager(registry, new FakeAzureClientFactory(adapter));
        manager.Connect(new AzureConnectionInfo("srv1", "My Azure", Container: "duetto"), new ConnectSecret());

        var store = new AzureConnectionStore(":mem:", _ => null, (_, _) => { });
        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, azureConnectionManager: manager, azureConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredAzureConnection?>();
        vm.OpenAzureConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToAzureShare(MakeStored("srv1", initialPath: "/duetto"), vm.Left);

        Assert.Equal("azure://srv1/duetto", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_with_saved_key_navigates_and_no_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        var manager = new AzureConnectionManager(registry, new FakeAzureClientFactory(adapter));
        var codec = new SecretCodec();

        var info = new AzureConnectionInfo("srv1", "My Azure", Endpoint: "http://127.0.0.1:10000/devstoreaccount1",
            AccountName: "devstoreaccount1", AuthMode: AzureAuthMode.SharedKey, Container: "duetto", InitialPath: "/duetto");
        var stored = AzureConnectionStore.Pack(info, ConnectSecret.FromPassword("shh"), savePassword: true, codec);
        var store = new AzureConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, azureConnectionManager: manager, azureConnectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredAzureConnection?>();
        vm.OpenAzureConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToAzureShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("azure://srv1/duetto", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Anonymous_connects_without_dialog()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        var manager = new AzureConnectionManager(registry, new FakeAzureClientFactory(adapter));

        var stored = MakeStored("srv1", auth: AzureAuthMode.Anonymous);
        var store = new AzureConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, azureConnectionManager: manager, azureConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredAzureConnection?>();
        vm.OpenAzureConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToAzureShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("azure://srv1/duetto", vm.Left.CurrentPath);
        Assert.Empty(dialogsOpened);
    }

    [AvaloniaFact]
    public void Connect_auth_failure_opens_dialog_no_navigate()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var adapter = SeededAdapter();
        adapter.NextConnectThrow = new AzureAuthenticationException("bad");
        var manager = new AzureConnectionManager(registry, new FakeAzureClientFactory(adapter));
        var codec = new SecretCodec();

        var info = new AzureConnectionInfo("srv1", "My Azure", AccountName: "devstoreaccount1",
            AuthMode: AzureAuthMode.SharedKey, Container: "duetto", InitialPath: "/duetto");
        var stored = AzureConnectionStore.Pack(info, ConnectSecret.FromPassword("bad"), savePassword: true, codec);
        var store = new AzureConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, azureConnectionManager: manager, azureConnectionStore: store, codec: codec);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredAzureConnection?>();
        vm.OpenAzureConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        var before = vm.Left.CurrentPath;
        vm.ConnectToAzureShare(stored, vm.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(dialogsOpened);
        Assert.Equal(before, vm.Left.CurrentPath);
    }

    [AvaloniaFact]
    public void No_secret_opens_dialog_prefilled()
    {
        using var tmp = new TempDir();
        var registry = new FileSystemRegistry();
        var manager = new AzureConnectionManager(registry, new FakeAzureClientFactory(SeededAdapter()));

        var stored = MakeStored("srv1", auth: AzureAuthMode.SharedKey, savePassword: false);
        var store = new AzureConnectionStore(":mem:", _ => StoreJson(stored), (_, _) => { });

        using var vm = new MainViewModel(tmp.Path, tmp.Path,
            registry: registry, azureConnectionManager: manager, azureConnectionStore: store);

        vm.ConnectScheduler = work => { work(); return Task.CompletedTask; };
        var dialogsOpened = new List<StoredAzureConnection?>();
        vm.OpenAzureConnectDialog = (forEdit, _) => dialogsOpened.Add(forEdit);

        vm.ConnectToAzureShare(stored, vm.Left);

        Assert.Single(dialogsOpened);
        Assert.Equal("srv1", dialogsOpened[0]!.Id);
    }
}

public sealed class AzureSharesPopoverMergeTests
{
    private static StoredAzureConnection Azure(string id, string name) => new()
    {
        Id = id, Name = name, AccountName = "devstoreaccount1", AuthMode = AzureAuthMode.SharedKey, InitialPath = "/",
    };

    [AvaloniaFact]
    public void Popover_merges_azure_rows_tagged_by_scheme()
    {
        using var pane = new PaneViewModel("/tmp", new FileSystemRegistry());
        var popover = new DrivePopoverViewModel(pane)
        {
            ListAzureConnections = () => [Azure("c", "Azure One")],
        };

        popover.Refresh();

        var row = Assert.Single(popover.Shares);
        Assert.Equal("azure", row.Scheme);
        Assert.True(row.IsAzure);
        Assert.Equal("Azure", row.SchemeLabel);
    }

    [AvaloniaFact]
    public void EditShare_routes_azure_rows_to_the_azure_event()
    {
        using var pane = new PaneViewModel("/tmp", new FileSystemRegistry());
        var popover = new DrivePopoverViewModel(pane)
        {
            ListAzureConnections = () => [Azure("c", "Azure One")],
        };
        popover.Refresh();

        StoredAzureConnection? edited = null;
        popover.EditAzureShareRequested += s => edited = s;

        popover.EditShare(popover.Shares.Single());

        Assert.NotNull(edited);
        Assert.Equal("c", edited.Id);
    }
}
