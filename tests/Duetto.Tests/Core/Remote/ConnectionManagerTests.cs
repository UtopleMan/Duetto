using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Renci.SshNet.Common;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Unit tests for <see cref="ConnectionManager"/> using a fake client factory.
/// No sockets are opened; all operations are in-memory.
/// </summary>
public sealed class ConnectionManagerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DuettoConnectionInfo MakeInfo(string id = "conn1") =>
        new(id, "Test", "fake.local");

    private static ConnectSecret MakeSecret() => ConnectSecret.FromPassword("pw");

    /// <summary>
    /// Factory that returns the SAME <see cref="FakeSftpClientAdapter"/> on every call,
    /// so tests can inspect the adapter state (ConnectCount, etc.) after the manager acts.
    /// </summary>
    private sealed class SingleAdapterFactory(FakeSftpClientAdapter Adapter) : ISftpClientFactory
    {
        public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => Adapter;
    }

    private static (ConnectionManager Manager, FileSystemRegistry Registry, FakeSftpClientAdapter Adapter) Make(string id = "conn1")
    {
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter();
        var factory = new SingleAdapterFactory(adapter);
        var manager = new ConnectionManager(registry, store, factory);
        return (manager, registry, adapter);
    }

    // ── Connect — registers the provider ─────────────────────────────────────

    [Fact]
    public void Connect_registers_provider_in_registry()
    {
        var (manager, registry, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());

            // The provider must be resolvable via sftp://conn1/some/path
            var (provider, localPath) = registry.Resolve("sftp://conn1/some/path");
            Assert.NotNull(provider);
            Assert.Equal("/some/path", localPath);
        }
    }

    [Fact]
    public void Connect_marks_id_as_connected()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());
            Assert.True(manager.IsConnected("conn1"));
        }
    }

    [Fact]
    public void ConnectedIds_contains_connected_id()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo("alpha"), MakeSecret());
            manager.Connect(MakeInfo("beta"), MakeSecret());

            var ids = manager.ConnectedIds;
            Assert.Contains("alpha", ids);
            Assert.Contains("beta", ids);
            Assert.Equal(2, ids.Count);
        }
    }

    // ── Disconnect — unregisters the provider ────────────────────────────────

    [Fact]
    public void Disconnect_unregisters_provider_from_registry()
    {
        var (manager, registry, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());
            manager.Disconnect("conn1");

            Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/path"));
        }
    }

    [Fact]
    public void Disconnect_marks_id_as_not_connected()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());
            manager.Disconnect("conn1");

            Assert.False(manager.IsConnected("conn1"));
        }
    }

    [Fact]
    public void Disconnect_unknown_id_is_noop()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            // Must not throw for an id that was never connected.
            manager.Disconnect("nonexistent");
        }
    }

    [Fact]
    public void Disconnect_removes_id_from_ConnectedIds()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());
            manager.Disconnect("conn1");

            Assert.Empty(manager.ConnectedIds);
        }
    }

    // ── Replace-on-reconnect ─────────────────────────────────────────────────

    [Fact]
    public void Connect_second_time_same_id_disposes_old_and_replaces()
    {
        // Use two separate adapters to track dispose independently.
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();

        var adapter1 = new FakeSftpClientAdapter();
        var adapter2 = new FakeSftpClientAdapter();
        int callCount = 0;
        var factory = new DelegateFactory(_ =>
            callCount++ == 0 ? adapter1 : adapter2);

        using var manager = new ConnectionManager(registry, store, factory);

        manager.Connect(MakeInfo(), MakeSecret());

        // First adapter should be connected and registered.
        Assert.Equal(1, adapter1.ConnectCount);
        Assert.True(adapter1.IsConnected); // sanity: it's connected

        // Reconnect: should dispose adapter1 and register adapter2.
        manager.Connect(MakeInfo(), MakeSecret());

        // adapter1 disposed — IsConnected returns false because Dispose calls Disconnect.
        Assert.False(adapter1.IsConnected);

        // adapter2 is the live one.
        Assert.Equal(1, adapter2.ConnectCount);
        Assert.True(adapter2.IsConnected);

        // The registry should resolve to the NEW provider (backed by adapter2).
        var (provider, _) = registry.Resolve("sftp://conn1/");
        Assert.NotNull(provider);
    }

    private sealed class DelegateFactory(Func<DuettoConnectionInfo, ISftpClientAdapter> create) : ISftpClientFactory
    {
        public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => create(info);
    }

    // ── Failed Connect leaves nothing registered ─────────────────────────────

    [Fact]
    public void Connect_failure_leaves_no_registration_and_no_tracking()
    {
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter
        {
            NextConnectThrow = new SshAuthenticationException("bad credentials"),
        };
        var factory = new SingleAdapterFactory(adapter);
        using var manager = new ConnectionManager(registry, store, factory);

        Assert.Throws<SshAuthenticationException>(
            () => manager.Connect(MakeInfo(), MakeSecret()));

        // Nothing registered.
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/"));

        // Not tracked.
        Assert.False(manager.IsConnected("conn1"));
        Assert.Empty(manager.ConnectedIds);
    }

    [Fact]
    public void Connect_failure_after_existing_connection_unregisters_old_and_leaves_nothing()
    {
        // Scenario: connected, then reconnect attempt fails.
        // The old connection must be gone; the failed attempt must not register.
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();

        var adapter1 = new FakeSftpClientAdapter();
        int callCount = 0;
        var adapter2 = new FakeSftpClientAdapter
        {
            NextConnectThrow = new SshAuthenticationException("bad"),
        };
        var factory = new DelegateFactory(_ => callCount++ == 0 ? adapter1 : adapter2);
        using var manager = new ConnectionManager(registry, store, factory);

        // First connect succeeds.
        manager.Connect(MakeInfo(), MakeSecret());
        Assert.True(manager.IsConnected("conn1"));

        // Second connect attempt fails.
        Assert.Throws<SshAuthenticationException>(
            () => manager.Connect(MakeInfo(), MakeSecret()));

        // Old connection is gone (unregistered before the new attempt).
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/"));
        Assert.False(manager.IsConnected("conn1"));
        Assert.Empty(manager.ConnectedIds);
    }

    // ── DisposeAll / Dispose ─────────────────────────────────────────────────

    [Fact]
    public void DisposeAll_disconnects_and_unregisters_all()
    {
        var (manager, registry, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo("a"), MakeSecret());
            manager.Connect(MakeInfo("b"), MakeSecret());

            manager.DisposeAll();

            Assert.Empty(manager.ConnectedIds);
            Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://a/"));
            Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://b/"));
        }
    }

    [Fact]
    public void Dispose_disconnects_and_unregisters_all()
    {
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();
        var adapter = new FakeSftpClientAdapter();
        var factory = new SingleAdapterFactory(adapter);
        var manager = new ConnectionManager(registry, store, factory);

        manager.Connect(MakeInfo(), MakeSecret());
        Assert.True(manager.IsConnected("conn1"));

        manager.Dispose();

        Assert.False(adapter.IsConnected);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/"));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var (manager, _, _) = Make();
        manager.Connect(MakeInfo(), MakeSecret());

        manager.Dispose();
        manager.Dispose(); // second Dispose must not throw
    }

    // ── IsConnected / ConnectedIds edge cases ────────────────────────────────

    [Fact]
    public void IsConnected_returns_false_for_unknown_id()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            Assert.False(manager.IsConnected("never-seen"));
        }
    }

    [Fact]
    public void ConnectedIds_is_empty_before_any_connect()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            Assert.Empty(manager.ConnectedIds);
        }
    }

    [Fact]
    public void Connect_on_disposed_manager_throws_ObjectDisposedException()
    {
        var (manager, _, _) = Make();
        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => manager.Connect(MakeInfo(), MakeSecret()));
    }

    // ── lock scope during the handshake ──────────────────────────────────────

    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task State_queries_respond_while_handshake_is_blocked()
    {
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();
        using var entered = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        var adapter = new FakeSftpClientAdapter { ConnectEntered = entered, ConnectGate = gate };
        var factory = new SingleAdapterFactory(adapter);
        using var manager = new ConnectionManager(registry, store, factory);

        var connectTask = Task.Run(() => manager.Connect(MakeInfo(), MakeSecret()));
        try
        {
            Assert.True(entered.Wait(GateTimeout), "handshake never started");

            // While the handshake is blocked, state queries must NOT block on the manager
            // lock.  Each runs on its own task; WaitAsync throws TimeoutException on a
            // regression instead of hanging the test run.
            Assert.False(await Task.Run(() => manager.IsConnected("conn1")).WaitAsync(GateTimeout));
            Assert.Empty(await Task.Run(() => manager.ConnectedIds).WaitAsync(GateTimeout));
            await Task.Run(() => manager.Disconnect("other-id")).WaitAsync(GateTimeout);
        }
        finally
        {
            gate.Set(); // always release so connectTask cannot leak a blocked thread
        }

        // Release the handshake: the connect must now complete and register.
        await connectTask;
        Assert.True(manager.IsConnected("conn1"));
        var (provider, _) = registry.Resolve("sftp://conn1/");
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Dispose_during_handshake_disposes_fresh_connection_and_throws()
    {
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();
        using var entered = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        var adapter = new FakeSftpClientAdapter { ConnectEntered = entered, ConnectGate = gate };
        var factory = new SingleAdapterFactory(adapter);
        var manager = new ConnectionManager(registry, store, factory);

        var connectTask = Task.Run(() => manager.Connect(MakeInfo(), MakeSecret()));
        try
        {
            Assert.True(entered.Wait(GateTimeout), "handshake never started");

            // Dispose while the handshake is blocked — must not block on the manager lock.
            // WaitAsync throws TimeoutException on a regression instead of hanging the run.
            await Task.Run(manager.Dispose).WaitAsync(GateTimeout);
        }
        finally
        {
            gate.Set();
        }

        // The post-connect guard must dispose the fresh connection and throw.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connectTask);
        Assert.False(adapter.IsConnected);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/"));
    }
}
