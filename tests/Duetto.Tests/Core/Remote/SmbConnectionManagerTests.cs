using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class SmbConnectionManagerTests
{
    private static SmbConnectionInfo MakeInfo(string id = "conn1") => new(id, "Test", "fake.local");

    private static ConnectSecret MakeSecret() => ConnectSecret.FromPassword("pw");

    private sealed class DelegateSmbFactory(Func<SmbConnectionInfo, ISmbClientAdapter> create) : ISmbClientFactory
    {
        public ISmbClientAdapter Create(SmbConnectionInfo info, ConnectSecret secret) => create(info);
    }

    private static (SmbConnectionManager Manager, FileSystemRegistry Registry, FakeSmbClientAdapter Adapter) Make()
    {
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter();
        var manager = new SmbConnectionManager(registry, new FakeSmbFactory(adapter));
        return (manager, registry, adapter);
    }

    [Fact]
    public void Connect_registers_provider_under_smb_scheme()
    {
        var (manager, registry, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());

            var (provider, localPath) = registry.Resolve("smb://conn1/duetto/path");
            Assert.NotNull(provider);
            Assert.Equal("/duetto/path", localPath);
        }
    }

    [Fact]
    public void Connect_marks_id_connected_and_lists_it()
    {
        var (manager, _, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo("alpha"), MakeSecret());
            Assert.True(manager.IsConnected("alpha"));
            Assert.Contains("alpha", manager.ConnectedIds);
        }
    }

    [Fact]
    public void Disconnect_unregisters_and_marks_not_connected()
    {
        var (manager, registry, _) = Make();
        using (manager)
        {
            manager.Connect(MakeInfo(), MakeSecret());
            manager.Disconnect("conn1");

            Assert.False(manager.IsConnected("conn1"));
            Assert.Empty(manager.ConnectedIds);
            Assert.Throws<InvalidOperationException>(() => registry.Resolve("smb://conn1/path"));
        }
    }

    [Fact]
    public void Disconnect_unknown_id_is_noop()
    {
        var (manager, _, _) = Make();
        using (manager)
            manager.Disconnect("nonexistent");
    }

    [Fact]
    public void Connect_second_time_same_id_disposes_old_and_replaces()
    {
        var registry = new FileSystemRegistry();
        var adapter1 = new FakeSmbClientAdapter();
        var adapter2 = new FakeSmbClientAdapter();
        var callCount = 0;
        var factory = new DelegateSmbFactory(_ => callCount++ == 0 ? adapter1 : adapter2);
        using var manager = new SmbConnectionManager(registry, factory);

        manager.Connect(MakeInfo(), MakeSecret());
        Assert.True(adapter1.IsConnected);

        manager.Connect(MakeInfo(), MakeSecret());

        Assert.False(adapter1.IsConnected);
        Assert.True(adapter2.IsConnected);
        Assert.Equal(1, adapter2.ConnectCount);
        Assert.NotNull(registry.Resolve("smb://conn1/").Provider);
    }

    [Fact]
    public void Connect_failure_leaves_no_registration_and_no_tracking()
    {
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter { NextConnectThrow = new SmbAuthenticationException("bad creds") };
        using var manager = new SmbConnectionManager(registry, new FakeSmbFactory(adapter));

        Assert.Throws<SmbAuthenticationException>(() => manager.Connect(MakeInfo(), MakeSecret()));

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("smb://conn1/"));
        Assert.False(manager.IsConnected("conn1"));
        Assert.Empty(manager.ConnectedIds);
    }

    [Fact]
    public void Connect_failure_after_existing_unregisters_old_and_leaves_nothing()
    {
        var registry = new FileSystemRegistry();
        var adapter1 = new FakeSmbClientAdapter();
        var adapter2 = new FakeSmbClientAdapter { NextConnectThrow = new SmbAuthenticationException("bad") };
        var callCount = 0;
        var factory = new DelegateSmbFactory(_ => callCount++ == 0 ? adapter1 : adapter2);
        using var manager = new SmbConnectionManager(registry, factory);

        manager.Connect(MakeInfo(), MakeSecret());
        Assert.True(manager.IsConnected("conn1"));

        Assert.Throws<SmbAuthenticationException>(() => manager.Connect(MakeInfo(), MakeSecret()));

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("smb://conn1/"));
        Assert.False(manager.IsConnected("conn1"));
        Assert.Empty(manager.ConnectedIds);
    }

    [Fact]
    public void Dispose_disconnects_and_unregisters_all()
    {
        var registry = new FileSystemRegistry();
        var adapter = new FakeSmbClientAdapter();
        var manager = new SmbConnectionManager(registry, new FakeSmbFactory(adapter));

        manager.Connect(MakeInfo(), MakeSecret());
        manager.Dispose();

        Assert.False(adapter.IsConnected);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("smb://conn1/"));
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
    public void Connect_on_disposed_manager_throws()
    {
        var (manager, _, _) = Make();
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.Connect(MakeInfo(), MakeSecret()));
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
            Assert.Throws<InvalidOperationException>(() => registry.Resolve("smb://Server1/path"));
        }
    }

    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task State_queries_respond_while_handshake_is_blocked()
    {
        var registry = new FileSystemRegistry();
        using var entered = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        var adapter = new FakeSmbClientAdapter { ConnectEntered = entered, ConnectGate = gate };
        using var manager = new SmbConnectionManager(registry, new FakeSmbFactory(adapter));

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
    }
}
