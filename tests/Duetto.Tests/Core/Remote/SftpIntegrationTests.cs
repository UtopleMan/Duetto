using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

// Real-server SFTP smoke tests, gated on the DUETTO_SFTP_TEST env var so they never run in CI.
// xunit 2.x has no Assert.Skip; tests return early when the gate is unset — an implicit
// 0-assertion pass that keeps the regular suite green.
[Trait("Category", "Integration")]
public sealed class SftpIntegrationTests : IDisposable
{
    private readonly FileSystemRegistry _registry = new();
    private readonly ConnectionManager _manager;

    public SftpIntegrationTests()
    {
        _manager = new ConnectionManager(_registry, new HostKeyStore());
    }

    public void Dispose() => _manager.Dispose();

    // Returns false when the gate (DUETTO_SFTP_TEST) is closed — callers must return immediately.
    private static bool TryGetConfig(
        out DuettoConnectionInfo info,
        out ConnectSecret secret,
        out string testPath)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUETTO_SFTP_TEST")))
        {
            info = null!;
            secret = null!;
            testPath = null!;
            return false;
        }

        var host = Environment.GetEnvironmentVariable("DUETTO_SFTP_TEST_HOST") ?? "localhost";
        var portStr = Environment.GetEnvironmentVariable("DUETTO_SFTP_TEST_PORT") ?? "22";
        var user = Environment.GetEnvironmentVariable("DUETTO_SFTP_TEST_USER") ?? "test";
        var password = Environment.GetEnvironmentVariable("DUETTO_SFTP_TEST_PASSWORD") ?? "test";
        testPath = Environment.GetEnvironmentVariable("DUETTO_SFTP_TEST_PATH") ?? "/tmp/duetto-test";

        info = new DuettoConnectionInfo(
            Id: "integration",
            Name: "Integration Test Server",
            Host: host,
            Port: int.Parse(portStr),
            Username: user,
            AuthMode: AuthMode.Password,
            InitialRemotePath: testPath);

        secret = ConnectSecret.FromPassword(password);
        return true;
    }

    [Fact]
    public void Smoke_connect_list_write_read_rename_delete_disconnect()
    {
        if (!TryGetConfig(out var info, out var secret, out var testPath))
            return;

        _manager.Connect(info, secret);
        Assert.True(_manager.IsConnected("integration"));

        var (provider, _) = _registry.Resolve($"sftp://integration{testPath}");
        Assert.NotNull(provider);

        var runDir = $"duetto-run-{Guid.NewGuid():N}";
        var runPath = provider.CreateDirectory(testPath, runDir);

        var listing = provider.List(testPath);
        Assert.Contains(listing, e => e.Name == runDir && e.IsDirectory);

        var filePath = provider.CreateFile(runPath, "hello.txt");
        var content = "hello from Duetto integration test\n"u8.ToArray();
        using (var ws = provider.OpenWrite(filePath))
            ws.Write(content);

        using (var rs = provider.OpenRead(filePath))
        {
            var buf = new byte[content.Length];
            var read = rs.ReadAtLeast(buf, content.Length, throwOnEndOfStream: false);
            Assert.Equal(content.Length, read);
            Assert.Equal(content, buf[..read]);
        }

        var renamedPath = provider.Rename(filePath, "renamed.txt");
        Assert.Null(provider.Stat(filePath));
        Assert.NotNull(provider.Stat(renamedPath));

        provider.Delete(runPath, toTrash: false);
        Assert.False(provider.DirectoryExists(runPath));

        _manager.Disconnect("integration");
        Assert.False(_manager.IsConnected("integration"));
        Assert.Throws<InvalidOperationException>(() =>
            _registry.Resolve($"sftp://integration{testPath}"));
    }

    [Fact]
    public void ConnectedIds_tracks_live_connection()
    {
        if (!TryGetConfig(out var info, out var secret, out _))
            return;

        Assert.Empty(_manager.ConnectedIds);

        _manager.Connect(info, secret);
        Assert.Contains("integration", _manager.ConnectedIds);

        _manager.Disconnect("integration");
        Assert.DoesNotContain("integration", _manager.ConnectedIds);
    }
}
