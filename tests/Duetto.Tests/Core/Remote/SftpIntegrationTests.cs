using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Real-server SFTP integration smoke tests.
///
/// <para>
/// These tests are SKIPPED by default and must not run in the regular CI suite.
/// Set the <c>DUETTO_SFTP_TEST</c> environment variable to any non-empty value to enable them.
/// </para>
///
/// <para>
/// Required environment variables when <c>DUETTO_SFTP_TEST</c> is set:
/// <list type="bullet">
///   <item><description><c>DUETTO_SFTP_TEST_HOST</c> — hostname or IP of the SFTP server (default: localhost)</description></item>
///   <item><description><c>DUETTO_SFTP_TEST_PORT</c> — port number (default: 22)</description></item>
///   <item><description><c>DUETTO_SFTP_TEST_USER</c> — SSH username (default: test)</description></item>
///   <item><description><c>DUETTO_SFTP_TEST_PASSWORD</c> — password (default: test)</description></item>
///   <item><description><c>DUETTO_SFTP_TEST_PATH</c> — writable remote path for smoke ops (default: /tmp/duetto-test)</description></item>
/// </list>
/// </para>
///
/// <para>
/// xunit 2.x does not have <c>Assert.Skip</c>; tests return early when the gate env var is unset.
/// The early-return is an implicit pass with 0 assertions, which keeps the regular suite green.
/// </para>
/// </summary>
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

    // ── guard helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true and populates <paramref name="info"/> + <paramref name="secret"/> when the
    /// integration gate is open (<c>DUETTO_SFTP_TEST</c> is set).
    /// Returns false when the gate is closed — callers should return immediately.
    /// </summary>
    private static bool TryGetConfig(
        out DuettoConnectionInfo info,
        out ConnectSecret secret,
        out string testPath)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUETTO_SFTP_TEST")))
        {
            // xunit 2.x does not expose Assert.Skip; return false so the caller returns early.
            // The test is then a no-op pass, which keeps the default suite green.
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

    // ── smoke flow ───────────────────────────────────────────────────────────

    /// <summary>
    /// Full smoke: connect, list the initial path, create a directory + file,
    /// write + read back content, rename, delete, disconnect.
    /// </summary>
    [Fact]
    public void Smoke_connect_list_write_read_rename_delete_disconnect()
    {
        // Skip when DUETTO_SFTP_TEST is unset (xunit 2.x early-return pattern).
        if (!TryGetConfig(out var info, out var secret, out var testPath))
            return;

        // 1. Connect
        _manager.Connect(info, secret);
        Assert.True(_manager.IsConnected("integration"));

        // 2. Resolve the provider through the registry
        var (provider, _) = _registry.Resolve($"sftp://integration{testPath}");
        Assert.NotNull(provider);

        // 3. Create a unique working directory under testPath
        var runDir = $"duetto-run-{Guid.NewGuid():N}";
        var runPath = provider.CreateDirectory(testPath, runDir);

        // 4. List — the new directory should appear
        var listing = provider.List(testPath);
        Assert.Contains(listing, e => e.Name == runDir && e.IsDirectory);

        // 5. Create + write a file
        var filePath = provider.CreateFile(runPath, "hello.txt");
        var content = "hello from Duetto integration test\n"u8.ToArray();
        using (var ws = provider.OpenWrite(filePath))
            ws.Write(content);

        // 6. Read back and verify
        using (var rs = provider.OpenRead(filePath))
        {
            var buf = new byte[content.Length];
            var read = rs.ReadAtLeast(buf, content.Length, throwOnEndOfStream: false);
            Assert.Equal(content.Length, read);
            Assert.Equal(content, buf[..read]);
        }

        // 7. Rename the file
        var renamedPath = provider.Rename(filePath, "renamed.txt");
        Assert.Null(provider.Stat(filePath));
        Assert.NotNull(provider.Stat(renamedPath));

        // 8. Delete the run directory (recursive)
        provider.Delete(runPath, toTrash: false);
        Assert.False(provider.DirectoryExists(runPath));

        // 9. Disconnect — provider must become unavailable via registry
        _manager.Disconnect("integration");
        Assert.False(_manager.IsConnected("integration"));
        Assert.Throws<InvalidOperationException>(() =>
            _registry.Resolve($"sftp://integration{testPath}"));
    }

    /// <summary>
    /// Verifies that <see cref="ConnectionManager.ConnectedIds"/> tracks ids correctly
    /// across connect and disconnect on a live server.
    /// </summary>
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
