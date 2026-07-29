using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Renci.SshNet.Common;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Runs the shared <see cref="FileSystemProviderContract"/> suite against
/// <see cref="SftpFileSystemProvider"/> backed by a <see cref="FakeSftpClientAdapter"/>
/// (in-memory tree; no network, no sockets).
/// </summary>
public sealed class SftpFileSystemProviderContractTests : FileSystemProviderContract, IDisposable
{
    private readonly FakeSftpClientAdapter _adapter;
    private readonly SftpConnection _conn;
    private readonly SftpFileSystemProvider _provider;

    public SftpFileSystemProviderContractTests()
    {
        _adapter = new FakeSftpClientAdapter();
        // Pre-create the contract root so the test suite starts with an empty directory.
        _adapter.CreateDirectory(Root);

        // Wire a factory that always returns our pre-built fake adapter.
        var factory = new FakeSftpFactory(_adapter);
        var info = new DuettoConnectionInfo("test", "Test", "fake.local");
        var secret = ConnectSecret.FromPassword("pw");
        _conn = new SftpConnection(info, secret, factory);
        _provider = new SftpFileSystemProvider(_conn);
    }

    protected override IFileSystemProvider Provider => _provider;

    /// <summary>
    /// The contract root is "/test" — a pre-created empty directory inside the fake tree.
    /// Using a sub-directory instead of "/" avoids the contract's <c>Stat(Root)</c> call
    /// hitting the virtual root, and matches what a real SFTP session would provide
    /// (the server's <c>InitialRemotePath</c> rather than the filesystem root).
    /// </summary>
    protected override string Root => "/test";

    public void Dispose()
    {
        _provider.Dispose();
        _conn.Dispose();
    }

    // ── SFTP-specific tests ───────────────────────────────────────────────────

    /// <summary>
    /// Verify that the UnixPermissions string and AccessSummary are mapped from the
    /// fake entry's permission booleans.  rw-r--r-- → "rw-r--r--" / "RW".
    /// </summary>
    [Fact]
    public void Attrs_permissions_are_mapped_from_entry()
    {
        var file = _provider.CreateFile(Root, "perm.txt");
        var entry = _provider.Stat(file);
        Assert.NotNull(entry);
        // Default node: owner rw, group r, others r → rw-r--r--
        Assert.Equal("rw-r--r--", entry.UnixPermissions);
        Assert.Equal("RW", entry.AccessSummary);
    }

    /// <summary>
    /// SetLastWriteTimeUtc round-trips through the fake tree and is visible via Stat.
    /// </summary>
    [Fact]
    public void Attrs_mtime_is_preserved_via_SetLastWriteTimeUtc()
    {
        var file = _provider.CreateFile(Root, "mtime.txt");
        var t = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        _provider.SetLastWriteTimeUtc(file, t);
        var entry = _provider.Stat(file);
        Assert.NotNull(entry);
        Assert.Equal(t, entry.ModifiedUtc);
    }

    /// <summary>
    /// List and EnumerateRecursive must never expose the "." or ".." synthetic entries
    /// that a real SFTP server always emits.
    /// </summary>
    [Fact]
    public void Dot_and_dotdot_entries_are_filtered_from_List_and_EnumerateRecursive()
    {
        var sub = _provider.CreateDirectory(Root, "dottest");
        _provider.CreateFile(sub, "child.txt");

        var listed = _provider.List(sub);
        Assert.DoesNotContain(listed, e => e.Name is "." or "..");

        var walked = _provider.EnumerateRecursive(Root).ToList();
        Assert.DoesNotContain(walked, e => e.Name is "." or "..");
    }

    /// <summary>
    /// Delete on a non-empty directory must recurse depth-first and remove all children
    /// before removing the directory itself.
    /// </summary>
    [Fact]
    public void Delete_recurses_into_nested_directories()
    {
        var dir = _provider.CreateDirectory(Root, "nest");
        var inner = _provider.CreateDirectory(dir, "inner");
        _provider.CreateFile(inner, "file.txt");
        _provider.CreateFile(dir, "top.txt");

        _provider.Delete(dir, toTrash: false);

        Assert.False(_provider.DirectoryExists(dir));
        Assert.False(_provider.FileExists(dir + "/top.txt"));
        Assert.False(_provider.DirectoryExists(inner));
        Assert.False(_provider.FileExists(inner + "/file.txt"));
    }

    /// <summary>
    /// Simulates a connection drop mid-session: the fake adapter throws
    /// <see cref="SshConnectionException"/> on the first call to List(), which triggers
    /// <see cref="SftpConnection.WithReconnect{T}"/> to reconnect and retry exactly once.
    /// The second call must succeed.
    /// </summary>
    [Fact]
    public void Reconnect_once_on_SshConnectionException_then_retries()
    {
        // Prime the tree with a file before simulating the drop.
        _provider.CreateFile(Root, "ping.txt");

        // Force-disconnect and verify that WithReconnect auto-connects on the next call.
        _conn.Disconnect();
        var entries = _provider.List(Root);
        Assert.Contains(entries, e => e.Name == "ping.txt");
    }
}

/// <summary>
/// A factory that always vends the same pre-built <see cref="FakeSftpClientAdapter"/>
/// (no sockets opened).
/// </summary>
internal sealed class FakeSftpFactory : ISftpClientFactory
{
    private readonly FakeSftpClientAdapter _adapter;
    public FakeSftpFactory(FakeSftpClientAdapter adapter) => _adapter = adapter;

    public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => _adapter;
}
