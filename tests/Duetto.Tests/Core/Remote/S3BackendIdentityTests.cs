using Duetto.Core.Operations;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class S3BackendIdentityTests
{
    private static S3FileSystemProvider Make(string connId)
    {
        var adapter = new FakeS3ClientAdapter("duetto");
        var info = new S3ConnectionInfo(Id: connId, Name: "n");
        var conn = new S3Connection(info, new ConnectSecret(), new FakeS3ClientFactory(adapter));
        conn.Connect();
        return new S3FileSystemProvider(conn);
    }

    [Fact]
    public void BackendKey_is_null_at_the_bucket_list_root()
    {
        Assert.Null(Make("c1").BackendKey("/"));
    }

    [Fact]
    public void BackendKey_is_the_connection_domain_for_object_paths()
    {
        var provider = Make("c1");
        Assert.Equal("s3://c1", provider.BackendKey("/duetto/dir/file.txt"));
        Assert.Equal("s3://c1", provider.BackendKey("/other-bucket/x"));
    }

    [Fact]
    public void SameRenameDomain_true_for_two_providers_sharing_a_connection_id()
    {
        var a = Make("c1");
        var b = Make("c1");
        Assert.True(TransferEngine.SameRenameDomain(a, "/duetto/x", b, "/duetto/y"));
    }

    [Fact]
    public void SameRenameDomain_false_across_different_connections()
    {
        var a = Make("c1");
        var b = Make("c2");
        Assert.False(TransferEngine.SameRenameDomain(a, "/duetto/x", b, "/duetto/y"));
    }
}
