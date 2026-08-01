using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class SmbConnectionTests
{
    private static SmbConnection NewConnection(FakeSmbClientAdapter adapter) =>
        new(new SmbConnectionInfo("id", "Test", "fake.local"),
            ConnectSecret.FromPassword("pw"),
            new FakeSmbFactory(adapter));

    [Fact]
    public void WithReconnect_auto_connects_when_disconnected()
    {
        var adapter = new FakeSmbClientAdapter();
        using var conn = NewConnection(adapter);

        var result = conn.WithReconnect(() => 42);

        Assert.Equal(42, result);
        Assert.Equal(1, adapter.ConnectCount);
        Assert.True(conn.IsConnected);
    }

    [Fact]
    public void WithReconnect_reconnects_once_on_SmbConnectionException_then_retries()
    {
        var adapter = new FakeSmbClientAdapter();
        using var conn = NewConnection(adapter);

        var attempts = 0;
        var result = conn.WithReconnect(() =>
        {
            attempts++;
            if (attempts == 1)
                throw new SmbConnectionException("dropped");
            return "ok";
        });

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        // One initial connect + one reconnect.
        Assert.Equal(2, adapter.ConnectCount);
    }

    [Fact]
    public void WithReconnect_does_not_retry_on_authentication_failure()
    {
        var adapter = new FakeSmbClientAdapter();
        using var conn = NewConnection(adapter);

        Assert.Throws<SmbAuthenticationException>(() =>
            conn.WithReconnect<int>(() => throw new SmbAuthenticationException("bad creds")));

        // Connected once for the initial attempt; no reconnect on an auth failure.
        Assert.Equal(1, adapter.ConnectCount);
    }

    [Fact]
    public void WithReconnect_propagates_a_second_failure_without_further_retry()
    {
        var adapter = new FakeSmbClientAdapter();
        using var conn = NewConnection(adapter);

        Assert.Throws<SmbConnectionException>(() =>
            conn.WithReconnect<int>(() => throw new SmbConnectionException("still dropped")));

        // Initial connect + one reconnect, then the retry's failure propagates.
        Assert.Equal(2, adapter.ConnectCount);
    }

    [Fact]
    public void Failed_connect_leaves_the_connection_disconnected()
    {
        var adapter = new FakeSmbClientAdapter { NextConnectThrow = new SmbConnectionException("no route") };
        using var conn = NewConnection(adapter);

        Assert.Throws<SmbConnectionException>(() => conn.WithReconnect(() => 1));
        Assert.False(conn.IsConnected);
    }
}
