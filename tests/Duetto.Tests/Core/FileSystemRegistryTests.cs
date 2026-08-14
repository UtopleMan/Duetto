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

    [Fact]
    public async Task Concurrent_Resolve_during_Register_Unregister_does_not_throw()
    {
        const int Iterations = 200;

        var reg = new FileSystemRegistry();
        var fake = new InMemoryFileSystemProvider();

        var writerErrors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var readerErrors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        using var startGate = new ManualResetEventSlim(false);

        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            startGate.Wait();
            for (var i = 0; i < Iterations; i++)
            {
                try
                {
                    reg.Register("sftp", "c1", fake);
                    reg.Unregister("sftp", "c1");
                }
                catch (Exception ex)
                {
                    writerErrors.Add(ex);
                }
            }
        })).ToArray();

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            startGate.Wait();
            for (var i = 0; i < Iterations * 2; i++)
            {
                try
                {
                    reg.Resolve("sftp://c1/some/path");
                }
                catch (InvalidOperationException)
                {
                }
                catch (Exception ex)
                {
                    readerErrors.Add(ex);
                }
            }
        })).ToArray();

        startGate.Set();
        await Task.WhenAll([.. writers, .. readers]);

        Assert.Empty(writerErrors);
        Assert.Empty(readerErrors);
    }
}

public sealed class InMemoryProviderContractTests : FileSystemProviderContract
{
    protected override IFileSystemProvider Provider { get; } = new InMemoryFileSystemProvider();
    protected override string Root => "/";
}
