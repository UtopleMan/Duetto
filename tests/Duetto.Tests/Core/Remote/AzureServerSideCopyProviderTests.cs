using System.Text;
using Duetto.Core.Operations;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class AzureServerSideCopyProviderTests
{
    private static (AzureFileSystemProvider Provider, FakeAzureClientAdapter Adapter) Make(FakeAzureClientAdapter adapter)
    {
        var info = new AzureConnectionInfo(Id: "c1", Name: "n");
        var conn = new AzureConnection(info, new ConnectSecret(), new FakeAzureClientFactory(adapter));
        conn.Connect();
        return (new AzureFileSystemProvider(conn), adapter);
    }

    [Fact]
    public void TryServerSideCopy_copies_the_blob_and_reports_bytes()
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        adapter.Seed("duetto", "a.txt", [1, 2, 3, 4]);
        var (provider, _) = Make(adapter);

        long reported = 0;
        var ok = provider.TryServerSideCopy("/duetto/a.txt", "/duetto/b.txt", n => reported += n, CancellationToken.None);

        Assert.True(ok);
        Assert.True(provider.FileExists("/duetto/b.txt"));
        Assert.Equal(4, reported);
    }

    [Fact]
    public void TryServerSideCopy_returns_false_at_a_container_level_path()
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        var (provider, _) = Make(adapter);
        Assert.False(provider.TryServerSideCopy("/duetto", "/duetto2", _ => { }, CancellationToken.None));
    }

    [Fact]
    public void Engine_move_between_two_panes_of_one_connection_goes_server_side()
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        adapter.Seed("duetto", "a.txt", Encoding.UTF8.GetBytes("payload"));
        var factory = new FakeAzureClientFactory(adapter);
        var info = new AzureConnectionInfo(Id: "c1", Name: "n");

        var src = new AzureFileSystemProvider(Connect(info, factory));
        var dest = new AzureFileSystemProvider(Connect(info, factory));

        using var session = TransferEngine.Start(["/duetto/a.txt"], src, "/duetto/dst", dest, TransferMode.Move, "/duetto/dst");
        session.Completion.Wait();

        Assert.Null(session.Snapshot().FaultMessage);
        Assert.True(dest.FileExists("/duetto/dst/a.txt"));
        Assert.False(src.FileExists("/duetto/a.txt"));
        Assert.True(adapter.CopyCount >= 1);
        Assert.Equal(0, adapter.ReadCount);
    }

    private static AzureConnection Connect(AzureConnectionInfo info, FakeAzureClientFactory factory)
    {
        var conn = new AzureConnection(info, new ConnectSecret(), factory);
        conn.Connect();
        return conn;
    }
}
