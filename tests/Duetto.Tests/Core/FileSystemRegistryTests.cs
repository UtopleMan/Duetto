using Duetto.Core.FileSystem;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

public class FileSystemRegistryTests
{
    [Fact]
    public void Resolves_a_local_path_to_the_local_provider()
    {
        var reg = new FileSystemRegistry();
        var (provider, local) = reg.Resolve("/home/user");
        Assert.IsType<LocalFileSystemProvider>(provider);
        Assert.Equal("/home/user", local);
    }

    [Fact]
    public void Resolves_a_registered_remote_address_to_its_provider_and_local_path()
    {
        var reg = new FileSystemRegistry();
        var fake = new InMemoryFileSystemProvider();
        reg.Register("sftp", "c1", fake);

        var (provider, local) = reg.Resolve("sftp://c1/a/b");
        Assert.Same(fake, provider);
        Assert.Equal("/a/b", local);
    }

    [Fact]
    public void An_unregistered_remote_address_throws() =>
        Assert.Throws<InvalidOperationException>(() => new FileSystemRegistry().Resolve("sftp://ghost/a"));

    [Fact]
    public void Unregister_drops_the_provider()
    {
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "c1", new InMemoryFileSystemProvider());
        reg.Unregister("sftp", "c1");
        Assert.Throws<InvalidOperationException>(() => reg.Resolve("sftp://c1/a"));
    }
}

public sealed class InMemoryProviderContractTests : FileSystemProviderContract
{
    protected override IFileSystemProvider Provider { get; } = new InMemoryFileSystemProvider();
    protected override string Root => "/";
}
