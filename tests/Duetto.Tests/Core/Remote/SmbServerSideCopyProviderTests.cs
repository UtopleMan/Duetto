using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class SmbServerSideCopyProviderTests
{
    private static (SmbFileSystemProvider, FakeSmbClientAdapter) Connected()
    {
        var adapter = new FakeSmbClientAdapter();
        var conn = new SmbConnection(new SmbConnectionInfo("id", "n", "node108"),
            new ConnectSecret(""), new FakeSmbFactory(adapter));
        conn.Connect();
        return (new SmbFileSystemProvider(conn), adapter);
    }

    [Fact]
    public void TryServerSideCopy_forwards_to_adapter_and_copies()
    {
        var (provider, adapter) = Connected();
        adapter.CreateDirectory("/share");
        using (var w = adapter.OpenWrite("/share/a.bin")) w.Write(new byte[1234], 0, 1234);

        long reported = 0;
        var ok = ((IServerSideCopy)provider).TryServerSideCopy(
            "/share/a.bin", "/share/b.bin", n => reported += n, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(1234, reported);
        Assert.True(adapter.Exists("/share/b.bin"));
    }

    [Fact]
    public void TryServerSideCopy_returns_false_when_unsupported()
    {
        var (provider, adapter) = Connected();
        adapter.CreateDirectory("/share");
        using (var w = adapter.OpenWrite("/share/a.bin")) w.Write(new byte[10], 0, 10);
        adapter.ServerSideCopySupported = false;

        var ok = ((IServerSideCopy)provider).TryServerSideCopy(
            "/share/a.bin", "/share/b.bin", _ => { }, CancellationToken.None);

        Assert.False(ok);
    }
}
