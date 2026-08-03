using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class S3ConnectionManagerTests
{
    private static (S3ConnectionManager Manager, FileSystemRegistry Registry) Make()
    {
        var registry = new FileSystemRegistry();
        var adapter = new FakeS3ClientAdapter("duetto");
        return (new S3ConnectionManager(registry, new FakeS3ClientFactory(adapter)), registry);
    }

    private static S3ConnectionInfo Info(string id = "c1") => new(Id: id, Name: "n");

    [Fact]
    public void Connect_registers_a_resolvable_provider_under_the_s3_scheme()
    {
        var (manager, registry) = Make();
        manager.Connect(Info(), new ConnectSecret());

        Assert.True(manager.IsConnected("c1"));
        var (provider, localPath) = registry.Resolve("s3://c1/duetto/x.txt");
        Assert.IsType<S3FileSystemProvider>(provider);
        Assert.Equal("/duetto/x.txt", localPath);
    }

    [Fact]
    public void Disconnect_unregisters_the_provider()
    {
        var (manager, registry) = Make();
        manager.Connect(Info(), new ConnectSecret());
        manager.Disconnect("c1");

        Assert.False(manager.IsConnected("c1"));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("s3://c1/duetto/x.txt"));
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
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("s3://a/duetto/x"));
    }
}
