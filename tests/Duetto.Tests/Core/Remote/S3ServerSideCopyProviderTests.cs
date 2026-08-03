using System.Text;
using Duetto.Core.Operations;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class S3ServerSideCopyProviderTests
{
    private static (S3FileSystemProvider Provider, FakeS3ClientAdapter Adapter) Make(FakeS3ClientAdapter adapter)
    {
        var info = new S3ConnectionInfo(Id: "c1", Name: "n");
        var conn = new S3Connection(info, new ConnectSecret(), new FakeS3ClientFactory(adapter));
        conn.Connect();
        return (new S3FileSystemProvider(conn), adapter);
    }

    [Fact]
    public void TryServerSideCopy_copies_the_object_and_reports_bytes()
    {
        var adapter = new FakeS3ClientAdapter("duetto");
        adapter.Seed("duetto", "a.txt", [1, 2, 3, 4]);
        var (provider, _) = Make(adapter);

        long reported = 0;
        var ok = provider.TryServerSideCopy("/duetto/a.txt", "/duetto/b.txt", n => reported += n, CancellationToken.None);

        Assert.True(ok);
        Assert.True(provider.FileExists("/duetto/b.txt"));
        Assert.Equal(4, reported);
    }

    [Fact]
    public void TryServerSideCopy_returns_false_at_a_bucket_level_path()
    {
        var adapter = new FakeS3ClientAdapter("duetto");
        var (provider, _) = Make(adapter);
        Assert.False(provider.TryServerSideCopy("/duetto", "/duetto2", _ => { }, CancellationToken.None));
    }

    [Fact]
    public void Engine_move_between_two_panes_of_one_connection_goes_server_side()
    {
        var adapter = new FakeS3ClientAdapter("duetto");
        adapter.Seed("duetto", "a.txt", Encoding.UTF8.GetBytes("payload"));
        var factory = new FakeS3ClientFactory(adapter);
        var info = new S3ConnectionInfo(Id: "c1", Name: "n");

        var src = new S3FileSystemProvider(Connect(info, factory));
        var dest = new S3FileSystemProvider(Connect(info, factory));

        using var session = TransferEngine.Start(["/duetto/a.txt"], src, "/duetto/dst", dest, TransferMode.Move, "/duetto/dst");
        session.Completion.Wait();

        Assert.Null(session.Snapshot().FaultMessage);
        Assert.True(dest.FileExists("/duetto/dst/a.txt"));
        Assert.False(src.FileExists("/duetto/a.txt"));
        Assert.True(adapter.CopyCount >= 1);
        Assert.Equal(0, adapter.ReadCount); // no bytes streamed through the client
    }

    private static S3Connection Connect(S3ConnectionInfo info, FakeS3ClientFactory factory)
    {
        var conn = new S3Connection(info, new ConnectSecret(), factory);
        conn.Connect();
        return conn;
    }
}
