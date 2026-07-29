using Duetto.Core.Remote;
using Renci.SshNet;
using Renci.SshNet.Common;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Unit tests for <see cref="SftpConnection"/> using a fake client factory.
/// No sockets are opened.
/// </summary>
public class SftpConnectionTests
{
    private static DuettoConnectionInfo MakeInfo() =>
        new("id1", "Test", "host.test.local");

    private static ConnectSecret MakeSecret() => ConnectSecret.FromPassword("pw");

    // ── connect / disconnect / IsConnected ───────────────────────────────────

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
        // never connected — should be a no-op
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

    // ── reconnect logic ──────────────────────────────────────────────────────

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
        // deliberately not calling Connect

        conn.WithReconnect(() => { });

        // factory should have been called once (for the implicit connect)
        Assert.Equal(1, factory.CreateCount);
        Assert.True(conn.IsConnected);
    }

    [Fact]
    public void WithReconnect_reconnects_once_on_SshConnectionException_then_retries()
    {
        var factory = new FakeFactory();
        factory.OnOpThrow = new SshConnectionException("dropped");

        using var conn = new SftpConnection(MakeInfo(), MakeSecret(), factory);
        conn.Connect();

        var callCount = 0;
        conn.WithReconnect(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new SshConnectionException("dropped");
            // second call succeeds
        });

        // op was called twice (first throws, reconnects, retries)
        Assert.Equal(2, callCount);
        // factory.Create was called twice: initial connect + reconnect
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

        // op was called twice (initial + one retry after reconnect)
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

        // Should not retry for non-connection exceptions
        Assert.Equal(1, callCount);
    }

    // ── dispose ──────────────────────────────────────────────────────────────

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

    // ── host-key store wiring ────────────────────────────────────────────────

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

// ── fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeFactory : ISftpClientFactory
{
    public int CreateCount { get; private set; }
    public FakeAdapter? LastAdapter { get; private set; }

    /// <summary>When set, the fake adapter's Connect call will throw this.</summary>
    public Exception? OnConnectThrow { get; set; }

    /// <summary>Not used directly here but kept for future op-level throws.</summary>
    public Exception? OnOpThrow { get; set; }

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

    public ISftpClient Client =>
        throw new InvalidOperationException("Fake adapter has no real SftpClient.");

    public void Dispose()
    {
        IsDisposed = true;
        _connected = false;
    }
}
