using Duetto.Core.Remote;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

public class SftpProviderCarryOverTests
{
    private static SftpFileSystemProvider MakeProvider(
        ISftpClientAdapter adapter, out SftpConnection conn)
    {
        var factory = new SingleAdapterFactory(adapter);
        var info    = new DuettoConnectionInfo("carry", "Carry", "fake.local");
        var secret  = ConnectSecret.FromPassword("pw");
        conn = new SftpConnection(info, secret, factory);
        return new SftpFileSystemProvider(conn);
    }

    [Fact]
    public void ReplaceFile_posix_rejected_falls_back_to_delete_and_rename()
    {
        var adapter = new PosixProbeAdapter();
        // Root "/" is pre-populated by FakeSftpClientAdapter; don't re-create it.
        adapter.CreateFile("/src.part");
        adapter.CreateFile("/dst.txt");

        using var provider = MakeProvider(adapter, out var conn);

        provider.ReplaceFile("/src.part", "/dst.txt");

        Assert.True(adapter.Exists("/dst.txt"));
        Assert.False(adapter.Exists("/src.part"));
        Assert.True(adapter.PosixRenameAttempted);
        Assert.True(adapter.RegularRenameUsed);
        conn.Dispose();
    }

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

        provider.ReplaceFile("/a.part", "/a.txt");
        adapter.ResetCallTracking();

        provider.ReplaceFile("/b.part", "/b.txt");

        Assert.False(adapter.PosixRenameAttempted);
        Assert.True(adapter.RegularRenameUsed);
        conn.Dispose();
    }

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

    // Materialising the listing first prevents a reconnect mid-delete from re-enumerating
    // from the top with a stale iterator. The guard adapter's ListDirectory returns a lazy
    // sequence that throws if advanced after any delete call, so a lazy foreach-then-delete
    // walk trips it while the materialised (.ToList-first) walk does not.
    [Fact]
    public void DeleteRecursive_materializes_children_before_deleting()
    {
        var adapter = new EnumerationGuardAdapter();
        adapter.CreateDirectory("/dir");
        adapter.CreateFile("/dir/a.txt");
        adapter.CreateFile("/dir/b.txt");

        using var provider = MakeProvider(adapter, out var conn);

        provider.Delete("/dir", toTrash: false);

        Assert.False(adapter.Exists("/dir"));
        Assert.False(adapter.Exists("/dir/a.txt"));
        Assert.False(adapter.Exists("/dir/b.txt"));
        conn.Dispose();
    }

    private sealed class SingleAdapterFactory(ISftpClientAdapter adapter) : ISftpClientFactory
    {
        public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => adapter;
    }

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

        public bool IsConnected => true;
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

            _inner.RenameFile(oldPath, newPath, isPosix: false);
        }
    }

    private sealed class EnumerationGuardAdapter : ISftpClientAdapter
    {
        private readonly FakeSftpClientAdapter _inner = new();
        private int _deleteCount;

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
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public Stream OpenWrite(string path) => _inner.OpenWrite(path);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => _inner.SetLastWriteTimeUtc(path, utc);
        public void Dispose() => _inner.Dispose();

        public void DeleteFile(string path)
        {
            _deleteCount++;
            _inner.DeleteFile(path);
        }

        public void DeleteDirectory(string path)
        {
            _deleteCount++;
            _inner.DeleteDirectory(path);
        }

        public IEnumerable<SftpEntry> ListDirectory(string path) =>
            Guarded(_inner.ListDirectory(path).ToList());

        private IEnumerable<SftpEntry> Guarded(IReadOnlyList<SftpEntry> items)
        {
            var deletesAtStart = _deleteCount;
            foreach (var item in items)
            {
                if (_deleteCount != deletesAtStart)
                    throw new InvalidOperationException(
                        "ListDirectory enumeration advanced after a delete — " +
                        "DeleteRecursive must materialise the listing before deleting children.");
                yield return item;
            }
        }
    }
}
