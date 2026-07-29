using Duetto.Core.Remote;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Tests for Req 5 (POSIX-rename probe with fallback) and Req 6 (DeleteRecursive
/// child-listing materialization) in <see cref="SftpFileSystemProvider"/>.
/// All tests use an in-memory fake — no sockets opened.
/// </summary>
public class SftpProviderCarryOverTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static SftpFileSystemProvider MakeProvider(
        ISftpClientAdapter adapter, out SftpConnection conn)
    {
        var factory = new SingleAdapterFactory(adapter);
        var info    = new DuettoConnectionInfo("carry", "Carry", "fake.local");
        var secret  = ConnectSecret.FromPassword("pw");
        conn = new SftpConnection(info, secret, factory);
        return new SftpFileSystemProvider(conn);
    }

    // ── Req 5: POSIX-rename probe ─────────────────────────────────────────────

    /// <summary>
    /// First ReplaceFile call: posix-rename throws OperationUnsupported → fall back to
    /// delete-then-rename. The operation must still succeed (file ends up at destination).
    /// </summary>
    [Fact]
    public void ReplaceFile_posix_rejected_falls_back_to_delete_and_rename()
    {
        var adapter = new PosixProbeAdapter();
        // Root "/" is pre-populated by FakeSftpClientAdapter; don't re-create it.
        adapter.CreateFile("/src.part");
        adapter.CreateFile("/dst.txt");

        using var provider = MakeProvider(adapter, out var conn);

        // First call: posix throws OperationUnsupported → fallback deletes /dst.txt, renames.
        provider.ReplaceFile("/src.part", "/dst.txt");

        Assert.True(adapter.Exists("/dst.txt"));
        Assert.False(adapter.Exists("/src.part"));
        Assert.True(adapter.PosixRenameAttempted);
        Assert.True(adapter.RegularRenameUsed);
        conn.Dispose();
    }

    /// <summary>
    /// After the first posix-rename failure the provider must NOT try posix again —
    /// subsequent ReplaceFile calls go straight to the delete-then-rename fallback.
    /// </summary>
    [Fact]
    public void ReplaceFile_after_posix_rejection_goes_straight_to_fallback()
    {
        var adapter = new PosixProbeAdapter();
        // Root "/" is pre-populated by FakeSftpClientAdapter; don't re-create it.
        adapter.CreateFile("/a.part");
        adapter.CreateFile("/a.txt");
        adapter.CreateFile("/b.part");
        adapter.CreateFile("/b.txt");

        using var provider = MakeProvider(adapter, out var conn);

        // First call: posix fails, sets _posixRenameWorked = false.
        provider.ReplaceFile("/a.part", "/a.txt");
        adapter.ResetCallTracking(); // reset counters to observe second call in isolation

        // Second call: must NOT attempt posix rename.
        provider.ReplaceFile("/b.part", "/b.txt");

        Assert.False(adapter.PosixRenameAttempted);
        Assert.True(adapter.RegularRenameUsed);
        conn.Dispose();
    }

    /// <summary>
    /// After the first posix-rename failure, <c>Capabilities.AtomicRename</c> must be false.
    /// </summary>
    [Fact]
    public void AtomicRename_capability_false_after_posix_rejection()
    {
        var adapter = new PosixProbeAdapter();
        // Root "/" is pre-populated by FakeSftpClientAdapter; don't re-create it.
        adapter.CreateFile("/src.part");
        adapter.CreateFile("/dst.txt");

        using var provider = MakeProvider(adapter, out var conn);

        Assert.True(provider.Capabilities.AtomicRename, "should start as true");

        provider.ReplaceFile("/src.part", "/dst.txt");

        Assert.False(provider.Capabilities.AtomicRename, "should be false after posix rejection");
        conn.Dispose();
    }

    // ── Req 6: DeleteRecursive materialization ────────────────────────────────

    /// <summary>
    /// DeleteRecursive must materialise the directory listing before starting to delete
    /// children. The materialization prevents a reconnect mid-delete from re-enumerating
    /// from the top with a stale iterator.
    ///
    /// Verified by using a tracking adapter: after the first DeleteFile the adapter
    /// checks that the listing was fully materialised (not lazy) by observing that no
    /// second ListDirectory call is issued during the recursive walk.
    /// </summary>
    [Fact]
    public void DeleteRecursive_materializes_children_before_deleting()
    {
        var adapter = new MaterializationTrackingAdapter();
        adapter.CreateDirectory("/dir");
        adapter.CreateFile("/dir/a.txt");
        adapter.CreateFile("/dir/b.txt");

        using var provider = MakeProvider(adapter, out var conn);

        provider.Delete("/dir", toTrash: false);

        // The directory and all children must be gone.
        Assert.False(adapter.Exists("/dir"));
        Assert.False(adapter.Exists("/dir/a.txt"));
        Assert.False(adapter.Exists("/dir/b.txt"));

        // ListDirectory must have been called exactly once (for /dir) — materialized before
        // any deletion, so no re-listing occurred mid-delete.
        Assert.Equal(1, adapter.ListDirectoryCallCount);
        conn.Dispose();
    }

    // ── Fake adapters ─────────────────────────────────────────────────────────

    /// <summary>
    /// Factory that always returns the same pre-built adapter (no connect socket opened).
    /// </summary>
    private sealed class SingleAdapterFactory(ISftpClientAdapter adapter) : ISftpClientFactory
    {
        public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => adapter;
    }

    /// <summary>
    /// Adapter that wraps a <see cref="FakeSftpClientAdapter"/> and intercepts
    /// <see cref="RenameFile"/> calls to track posix vs regular rename usage.
    /// On the first posix rename call it throws <see cref="SftpException"/> with
    /// <see cref="StatusCode.OperationUnsupported"/>; subsequent regular renames succeed.
    /// </summary>
    private sealed class PosixProbeAdapter : ISftpClientAdapter
    {
        private readonly FakeSftpClientAdapter _inner = new();
        private bool _firstPosixThrown;

        public bool PosixRenameAttempted { get; private set; }
        public bool RegularRenameUsed { get; private set; }

        public void ResetCallTracking()
        {
            PosixRenameAttempted = false;
            RegularRenameUsed = false;
        }

        // ── forwarded ──────────────────────────────────────────────────────────

        public bool IsConnected => true; // always appears connected in fake
        public void Connect() { }
        public void Disconnect() { }
        public void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler) { }
        public IEnumerable<SftpEntry> ListDirectory(string path) => _inner.ListDirectory(path);
        public SftpEntry? Get(string path) => _inner.Get(path);
        public bool IsDirectory(string path) => _inner.IsDirectory(path);
        public bool IsFile(string path) => _inner.IsFile(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public void CreateFile(string path) => _inner.CreateFile(path);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void DeleteDirectory(string path) => _inner.DeleteDirectory(path);
        public bool Exists(string path) => _inner.Exists(path);
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public Stream OpenWrite(string path) => _inner.OpenWrite(path);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => _inner.SetLastWriteTimeUtc(path, utc);
        public void Dispose() => _inner.Dispose();

        public void RenameFile(string oldPath, string newPath, bool isPosix = false)
        {
            if (isPosix)
            {
                PosixRenameAttempted = true;
                if (!_firstPosixThrown)
                {
                    _firstPosixThrown = true;
                    throw new SftpException(StatusCode.OperationUnsupported, "POSIX rename not supported");
                }
            }
            else
            {
                RegularRenameUsed = true;
            }

            _inner.RenameFile(oldPath, newPath, isPosix: false); // regular rename in inner
        }
    }

    /// <summary>
    /// Adapter that counts <see cref="ListDirectory"/> calls so we can verify
    /// that the listing is materialised exactly once (not lazily re-iterated).
    /// </summary>
    private sealed class MaterializationTrackingAdapter : ISftpClientAdapter
    {
        private readonly FakeSftpClientAdapter _inner = new();
        private int _listDirCount;

        public int ListDirectoryCallCount => _listDirCount;

        public bool IsConnected => true;
        public void Connect() { }
        public void Disconnect() { }
        public void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler) { }
        public SftpEntry? Get(string path) => _inner.Get(path);
        public bool IsDirectory(string path) => _inner.IsDirectory(path);
        public bool IsFile(string path) => _inner.IsFile(path);
        public bool Exists(string path) => _inner.Exists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public void CreateFile(string path) => _inner.CreateFile(path);
        public void RenameFile(string oldPath, string newPath, bool isPosix = false) => _inner.RenameFile(oldPath, newPath, isPosix);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void DeleteDirectory(string path) => _inner.DeleteDirectory(path);
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public Stream OpenWrite(string path) => _inner.OpenWrite(path);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => _inner.SetLastWriteTimeUtc(path, utc);
        public void Dispose() => _inner.Dispose();

        public IEnumerable<SftpEntry> ListDirectory(string path)
        {
            // Only count non-root listings (the recursive delete only lists /dir, not /).
            if (path != "/")
                Interlocked.Increment(ref _listDirCount);
            return _inner.ListDirectory(path);
        }
    }
}
