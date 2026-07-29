using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class ConnectionInfoTests
{
    [Fact]
    public void Default_port_is_22()
    {
        var info = new ConnectionInfo("id1", "My Server", "example.com", Username: "alice");
        Assert.Equal(22, info.Port);
    }

    [Fact]
    public void Default_auth_mode_is_Password()
    {
        var info = new ConnectionInfo("id1", "My Server", "example.com");
        Assert.Equal(AuthMode.Password, info.AuthMode);
    }

    [Fact]
    public void Default_key_path_is_null()
    {
        var info = new ConnectionInfo("id1", "My Server", "example.com");
        Assert.Null(info.KeyPath);
    }

    [Fact]
    public void Default_initial_remote_path_is_root()
    {
        var info = new ConnectionInfo("id1", "My Server", "example.com");
        Assert.Equal("/", info.InitialRemotePath);
    }

    [Fact]
    public void Record_equality_compares_all_fields()
    {
        var a = new ConnectionInfo("id1", "My Server", "host.example.com", 2222, "alice", AuthMode.Key, "/home/alice/.ssh/id_ed25519", "/home/alice");
        var b = new ConnectionInfo("id1", "My Server", "host.example.com", 2222, "alice", AuthMode.Key, "/home/alice/.ssh/id_ed25519", "/home/alice");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ConnectSecret_password_factory_sets_password()
    {
        var s = ConnectSecret.FromPassword("hunter2");
        Assert.Equal("hunter2", s.Password);
        Assert.Null(s.KeyPassphrase);
    }

    [Fact]
    public void ConnectSecret_key_factory_sets_passphrase()
    {
        var s = ConnectSecret.FromKey("mypass");
        Assert.Null(s.Password);
        Assert.Equal("mypass", s.KeyPassphrase);
    }

    [Fact]
    public void ConnectSecret_key_factory_null_passphrase_is_valid()
    {
        var s = ConnectSecret.FromKey();
        Assert.Null(s.KeyPassphrase);
    }
}
