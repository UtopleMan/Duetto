using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class SmbBackendIdentityTests
{
    private static SmbFileSystemProvider Provider(string host)
    {
        var info = new SmbConnectionInfo(Id: host, Name: host, Host: host);
        var conn = new SmbConnection(info, new ConnectSecret(""), new FakeSmbFactory(new FakeSmbClientAdapter()));
        conn.Connect();
        return new SmbFileSystemProvider(conn);
    }

    [Fact]
    public void BackendKey_is_host_and_share_lowercased()
    {
        var p = (IBackendIdentity)Provider("Node108");
        Assert.Equal("smb://node108/data", p.BackendKey("/Data/dir/file.txt"));
        Assert.Equal("smb://node108/data", p.BackendKey("/Data"));
    }

    [Fact]
    public void BackendKey_is_null_for_share_root()
    {
        var p = (IBackendIdentity)Provider("node108");
        Assert.Null(p.BackendKey("/"));
        Assert.Null(p.BackendKey(""));
    }

    [Fact]
    public void Same_host_and_share_two_instances_have_equal_keys()
    {
        var a = (IBackendIdentity)Provider("node108");
        var b = (IBackendIdentity)Provider("node108");
        Assert.Equal(a.BackendKey("/data/x"), b.BackendKey("/data/y"));
        Assert.NotNull(a.BackendKey("/data/x"));
    }

    [Fact]
    public void Different_share_or_host_have_different_keys()
    {
        var a = (IBackendIdentity)Provider("node108");
        var b = (IBackendIdentity)Provider("node109");
        Assert.NotEqual(a.BackendKey("/data/x"), a.BackendKey("/other/x"));
        Assert.NotEqual(a.BackendKey("/data/x"), b.BackendKey("/data/x"));
    }
}
