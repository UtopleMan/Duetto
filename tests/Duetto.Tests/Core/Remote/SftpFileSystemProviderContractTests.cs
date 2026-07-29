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
    /// Exercises WithReconnect's !IsConnected branch: after an explicit Disconnect the
    /// next provider call auto-connects and succeeds.
    /// </summary>
    [Fact]
    public void List_auto_connects_after_explicit_disconnect()
    {
        // Prime the tree with a file before dropping the connection.
        _provider.CreateFile(Root, "ping.txt");

        _conn.Disconnect();
        var entries = _provider.List(Root);
        Assert.Contains(entries, e => e.Name == "ping.txt");
    }

    /// <summary>
    /// Exercises WithReconnect's SshConnectionException-catch branch: the fake adapter
    /// throws <see cref="SshConnectionException"/> from ListDirectory exactly once
    /// (simulating a mid-operation connection drop).  WithReconnect must reconnect
    /// exactly once and retry; the retried op's result must reach the caller.
    /// </summary>
    [Fact]
    public void Reconnect_once_on_SshConnectionException_then_retries()
    {
        // Prime the tree with a file (this lazily connects: ConnectCount becomes 1).
        _provider.CreateFile(Root, "ping.txt");
        Assert.Equal(1, _adapter.ConnectCount);

        // One-shot connection drop on the next ListDirectory enumeration.
        _adapter.NextListThrow = new SshConnectionException("dropped mid-list");

        var entries = _provider.List(Root);

        // The retried op succeeded and exactly one reconnect happened.
        Assert.Contains(entries, e => e.Name == "ping.txt");
        Assert.Equal(2, _adapter.ConnectCount);
    }

    /// <summary>
    /// A per-directory low-level <see cref="SshException"/> (not a connection drop) must
    /// not abort the whole recursive walk: the failing directory's contents are skipped
    /// and the rest of the tree is still yielded.
    /// </summary>
    [Fact]
    public void EnumerateRecursive_skips_directory_that_throws_SshException_and_continues()
    {
        var okDir = _provider.CreateDirectory(Root, "ok");
        _provider.CreateFile(okDir, "visible.txt");
        var badDir = _provider.CreateDirectory(Root, "bad");
        _provider.CreateFile(badDir, "hidden.txt");

        // Every ListDirectory on badDir throws a low-level SFTP protocol error.
        _adapter.ListThrowsByPath[badDir] = new SshException("SFTP protocol error");

        var names = _provider.EnumerateRecursive(Root).Select(e => e.Name).ToList();

        Assert.Contains("ok", names);
        Assert.Contains("visible.txt", names);
        Assert.Contains("bad", names);            // the entry itself comes from listing Root
        Assert.DoesNotContain("hidden.txt", names); // but its contents are skipped
    }

    // ── Finding 6: SshAuthenticationException must not be swallowed ──────────

    /// <summary>
    /// An <see cref="SshAuthenticationException"/> thrown while listing a subdirectory must
    /// propagate out of <c>EnumerateRecursive</c> rather than being silently swallowed.
    /// Swallowing it would silently truncate search results when a mid-walk reconnect fails auth.
    /// </summary>
    [Fact]
    public void EnumerateRecursive_propagates_SshAuthenticationException_from_subdirectory()
    {
        var okDir = _provider.CreateDirectory(Root, "good");
        _provider.CreateFile(okDir, "file.txt");
        var authFailDir = _provider.CreateDirectory(Root, "authfail");

        // Any listing of authFailDir throws SshAuthenticationException.
        _adapter.ListThrowsByPath[authFailDir] =
            new SshAuthenticationException("Authentication failed");

        // The walk must propagate the auth exception rather than skipping the directory.
        Assert.Throws<SshAuthenticationException>(
            () => _provider.EnumerateRecursive(Root).ToList());
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
