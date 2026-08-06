using Duetto.Core.Operations;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class AzureBackendIdentityTests
{
    private static AzureFileSystemProvider Make(string connId)
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        var info = new AzureConnectionInfo(Id: connId, Name: "n");
        var conn = new AzureConnection(info, new ConnectSecret(), new FakeAzureClientFactory(adapter));
        conn.Connect();
        return new AzureFileSystemProvider(conn);
    }

    [Fact]
    public void BackendKey_is_null_at_the_container_list_root()
    {
        Assert.Null(Make("c1").BackendKey("/"));
    }

    [Fact]
    public void BackendKey_is_the_connection_domain_for_blob_paths()
    {
        var provider = Make("c1");
        Assert.Equal("azure://c1", provider.BackendKey("/duetto/dir/file.txt"));
        Assert.Equal("azure://c1", provider.BackendKey("/other-container/x"));
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
