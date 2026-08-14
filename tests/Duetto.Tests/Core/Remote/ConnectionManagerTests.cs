using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Renci.SshNet.Common;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

public sealed class ConnectionManagerTests
{
    private static DuettoConnectionInfo MakeInfo(string id = "conn1") =>
        new(id, "Test", "fake.local");

    private static ConnectSecret MakeSecret() => ConnectSecret.FromPassword("pw");

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

    [Fact]
    public void Connect_registers_provider_in_registry()
    {
        var (manager, registry, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());

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

    [Fact]
    public void Connect_second_time_same_id_disposes_old_and_replaces()
    {
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();

        var adapter1 = new FakeSftpClientAdapter();
        var adapter2 = new FakeSftpClientAdapter();
        int callCount = 0;
        var factory = new DelegateFactory(_ =>
            callCount++ == 0 ? adapter1 : adapter2);

        using var manager = new ConnectionManager(registry, store, factory);

        manager.Connect(MakeInfo(), MakeSecret());

        Assert.Equal(1, adapter1.ConnectCount);
        Assert.True(adapter1.IsConnected);

        manager.Connect(MakeInfo(), MakeSecret());

        Assert.False(adapter1.IsConnected);

        Assert.Equal(1, adapter2.ConnectCount);
        Assert.True(adapter2.IsConnected);

        var (provider, _) = registry.Resolve("sftp://conn1/");
        Assert.NotNull(provider);
    }

    private sealed class DelegateFactory(Func<DuettoConnectionInfo, ISftpClientAdapter> create) : ISftpClientFactory
    {
        public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => create(info);
    }

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

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/"));
        Assert.False(manager.IsConnected("conn1"));
        Assert.Empty(manager.ConnectedIds);
    }

    [Fact]
    public void Connect_failure_after_existing_connection_unregisters_old_and_leaves_nothing()
    {
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

        manager.Connect(MakeInfo(), MakeSecret());
        Assert.True(manager.IsConnected("conn1"));

        Assert.Throws<SshAuthenticationException>(
            () => manager.Connect(MakeInfo(), MakeSecret()));

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/"));
        Assert.False(manager.IsConnected("conn1"));
        Assert.Empty(manager.ConnectedIds);
    }

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
        manager.Dispose();
    }

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

    [Fact]
    public void Ids_are_case_insensitive_for_lookup_and_disconnect()
    {
        var (manager, registry, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo("Server1"), MakeSecret());

            Assert.True(manager.IsConnected("server1"));

            manager.Disconnect("SERVER1");

            Assert.Empty(manager.ConnectedIds);
            Assert.Throws<InvalidOperationException>(
                () => registry.Resolve("sftp://Server1/path"));
        }
    }

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

            Assert.False(await Task.Run(() => manager.IsConnected("conn1")).WaitAsync(GateTimeout));
            Assert.Empty(await Task.Run(() => manager.ConnectedIds).WaitAsync(GateTimeout));
            await Task.Run(() => manager.Disconnect("other-id")).WaitAsync(GateTimeout);
        }
        finally
        {
            gate.Set();
        }

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

            await Task.Run(manager.Dispose).WaitAsync(GateTimeout);
        }
        finally
        {
            gate.Set();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => connectTask);
        Assert.False(adapter.IsConnected);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("sftp://conn1/"));
    }

    [Fact]
    public async Task IsConnected_responds_while_Disconnect_is_blocked_on_graceful_close()
    {
        var registry = new FileSystemRegistry();
        var store = new HostKeyStore();
        using var disconnectEntered = new ManualResetEventSlim(false);
        using var disconnectGate = new ManualResetEventSlim(false);

        var adapter = new FakeSftpClientAdapter
        {
            DisconnectEntered = disconnectEntered,
            DisconnectGate = disconnectGate,
        };
        var factory = new SingleAdapterFactory(adapter);
        using var manager = new ConnectionManager(registry, store, factory);

        manager.Connect(MakeInfo(), MakeSecret());
        Assert.True(manager.IsConnected("conn1"));

        var disconnectTask = Task.Run(() => manager.Disconnect("conn1"));

        try
        {
            Assert.True(disconnectEntered.Wait(GateTimeout), "Disconnect never entered adapter");

            Assert.False(await Task.Run(() => manager.IsConnected("conn1")).WaitAsync(GateTimeout));
            Assert.Empty(await Task.Run(() => manager.ConnectedIds).WaitAsync(GateTimeout));
        }
        finally
        {
            disconnectGate.Set();
        }

        await disconnectTask;
    }
}
