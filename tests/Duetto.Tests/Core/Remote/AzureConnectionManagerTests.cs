using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class AzureConnectionManagerTests
{
    private static (AzureConnectionManager Manager, FileSystemRegistry Registry) Make()
    {
        var registry = new FileSystemRegistry();
        var adapter = new FakeAzureClientAdapter("duetto");
        return (new AzureConnectionManager(registry, new FakeAzureClientFactory(adapter)), registry);
    }

    private static AzureConnectionInfo Info(string id = "c1") => new(Id: id, Name: "n");

    [Fact]
    public void Connect_registers_a_resolvable_provider_under_the_azure_scheme()
    {
        var (manager, registry) = Make();
        manager.Connect(Info(), new ConnectSecret());

        Assert.True(manager.IsConnected("c1"));
        var (provider, localPath) = registry.Resolve("azure://c1/duetto/x.txt");
        Assert.IsType<AzureFileSystemProvider>(provider);
        Assert.Equal("/duetto/x.txt", localPath);
    }

    [Fact]
    public void Disconnect_unregisters_the_provider()
    {
        var (manager, registry) = Make();
        manager.Connect(Info(), new ConnectSecret());
        manager.Disconnect("c1");

        Assert.False(manager.IsConnected("c1"));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("azure://c1/duetto/x.txt"));
    }

    [Fact]
    public void Reconnecting_the_same_id_replaces_the_registration()
    {
        var (manager, _) = Make();
        manager.Connect(Info(), new ConnectSecret());
        manager.Connect(Info(), new ConnectSecret());

        Assert.Single(manager.ConnectedIds);
    }

    [Fact]
    public void DisposeAll_clears_every_registration()
    {
        var (manager, registry) = Make();
        manager.Connect(Info("a"), new ConnectSecret());
        manager.Connect(Info("b"), new ConnectSecret());

        manager.DisposeAll();

        Assert.Empty(manager.ConnectedIds);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("azure://a/duetto/x"));
    }
}
