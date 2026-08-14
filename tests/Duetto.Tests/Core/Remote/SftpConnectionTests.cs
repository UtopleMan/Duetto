using Duetto.Core.Remote;
using Renci.SshNet;
using Renci.SshNet.Common;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

public class SftpConnectionTests
{
    private static DuettoConnectionInfo MakeInfo() =>
        new("id1", "Test", "host.test.local");

    private static ConnectSecret MakeSecret() => ConnectSecret.FromPassword("pw");

    [Fact]
    public void IsConnected_false_before_Connect()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        Assert.False(conn.IsConnected);
    }

    [Fact]
    public void IsConnected_true_after_Connect()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();
        Assert.True(conn.IsConnected);
    }

    [Fact]
    public void IsConnected_false_after_Disconnect()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();
        conn.Disconnect();
        Assert.False(conn.IsConnected);
    }

    [Fact]
    public void Disconnect_when_already_disconnected_does_not_throw()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Disconnect();
    }

    [Fact]
    public void Connect_creates_new_adapter_via_factory()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void Connect_failure_disposes_the_fresh_adapter_and_stays_disconnected()
    {
        var factory = new FakeFactory
        {
            OnConnectThrow = new SshConnectionException("handshake failed"),
        };
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);

        Assert.Throws<SshConnectionException>(() => conn.Connect());

        Assert.True(factory.LastAdapter!.IsDisposed);
        Assert.False(conn.IsConnected);
        Assert.Throws<InvalidOperationException>(() => conn.Adapter);
    }

    [Fact]
    public void WithReconnect_calls_op_when_connected()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();

        var called = false;
        conn.WithReconnect(() => { called = true; });
        Assert.True(called);
    }

    [Fact]
    public void WithReconnect_connects_automatically_when_not_connected()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);

        conn.WithReconnect(() => { });

        Assert.Equal(1, factory.CreateCount);
        Assert.True(conn.IsConnected);
    }

    [Fact]
    public void WithReconnect_reconnects_once_on_SshConnectionException_then_retries()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();

        var callCount = 0;
        conn.WithReconnect(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new SshConnectionException("dropped");
        });

        Assert.Equal(2, callCount);
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void WithReconnect_propagates_SshConnectionException_on_second_failure()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();

        var callCount = 0;
        Assert.Throws<SshConnectionException>(() =>
            conn.WithReconnect(() =>
            {
                callCount++;
                throw new SshConnectionException("always fails");
            }));

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void WithReconnect_returns_value_from_op()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();

        var result = conn.WithReconnect(() => 42);
        Assert.Equal(42, result);
    }

    [Fact]
    public void WithReconnect_propagates_non_connection_exceptions_without_retry()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();

        var callCount = 0;
        Assert.Throws<InvalidOperationException>(() =>
            conn.WithReconnect(() =>
            {
                callCount++;
                throw new InvalidOperationException("not a connection error");
            }));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Dispose_cleans_up_adapter()
    {
        var factory = new FakeFactory();
        var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();
        conn.Dispose();
        Assert.True(factory.LastAdapter!.IsDisposed);
    }

    [Fact]
    public void Connect_after_Dispose_throws_ObjectDisposedException()
    {
        var factory = new FakeFactory();
        var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Dispose();
        Assert.Throws<ObjectDisposedException>(() => conn.Connect());
    }

    [Fact]
    public void WithReconnect_after_Dispose_throws_ObjectDisposedException()
    {
        var factory = new FakeFactory();
        var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Dispose();
        Assert.Throws<ObjectDisposedException>(() => conn.WithReconnect(() => { }));
    }

    [Fact]
    public void Connect_wires_HostKeyStore_handler_before_connecting()
    {
        var factory = new FakeFactory();
        var store = new HostKeyStore();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory, store);
        conn.Connect();
        Assert.True(factory.LastAdapter!.HostKeyHandlerWired);
    }

    [Fact]
    public void Connect_without_HostKeyStore_does_not_wire_handler()
    {
        var factory = new FakeFactory();
        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory, hostKeyStore: null);
        conn.Connect();
        Assert.False(factory.LastAdapter!.HostKeyHandlerWired);
    }
}

internal sealed class FakeFactory : ISftpClientFactory
{
    public int CreateCount { get; private set; }
    public FakeAdapter? LastAdapter { get; private set; }

    public Exception? OnConnectThrow { get; set; }

    public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret)
    {
        CreateCount++;
        LastAdapter = new FakeAdapter(OnConnectThrow);
        return LastAdapter;
    }
}

internal sealed class FakeAdapter : ISftpClientAdapter
{
    private bool _connected;
    private readonly Exception? _connectThrow;

    public bool IsDisposed { get; private set; }
    public bool HostKeyHandlerWired { get; private set; }

    public FakeAdapter(Exception? connectThrow = null)
        => _connectThrow = connectThrow;

    public bool IsConnected => _connected;

    public void Connect()
    {
        if (_connectThrow is not null) throw _connectThrow;
        _connected = true;
    }

    public void Disconnect() => _connected = false;

    public void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler)
        => HostKeyHandlerWired = true;

    public IEnumerable<Duetto.Core.Remote.SftpEntry> ListDirectory(string path) => throw new NotSupportedException();
    public Duetto.Core.Remote.SftpEntry? Get(string path) => throw new NotSupportedException();
    public bool IsDirectory(string path) => throw new NotSupportedException();
    public bool IsFile(string path) => throw new NotSupportedException();
    public void CreateDirectory(string path) => throw new NotSupportedException();
    public void CreateFile(string path) => throw new NotSupportedException();
    public void RenameFile(string oldPath, string newPath, bool isPosix = false) => throw new NotSupportedException();
    public void DeleteFile(string path) => throw new NotSupportedException();
    public void DeleteDirectory(string path) => throw new NotSupportedException();
    public bool Exists(string path) => throw new NotSupportedException();
    public Stream OpenRead(string path) => throw new NotSupportedException();
    public Stream OpenWrite(string path) => throw new NotSupportedException();
    public void SetLastWriteTimeUtc(string path, DateTime utc) => throw new NotSupportedException();

    public void Dispose()
    {
        IsDisposed = true;
        _connected = false;
    }
}
