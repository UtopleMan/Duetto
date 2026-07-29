using Duetto.Core.Remote;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// In-memory fake for <see cref="ISftpClientAdapter"/> used to test
/// <see cref="Duetto.Core.Remote.SftpFileSystemProvider"/> without any network.
///
/// <para>
/// The tree is keyed by normalized SFTP paths (always '/', no trailing slash except root).
/// File contents are stored as <c>byte[]</c> and returned via <see cref="MemoryStream"/>.
/// Streams returned by <see cref="OpenWrite"/> write back to the node when disposed.
/// </para>
///
/// <para>
/// <see cref="ListDirectory"/> emits "." and ".." entries to mirror a real SFTP server;
/// the provider is responsible for filtering them.
/// </para>
/// </summary>
internal sealed class FakeSftpClientAdapter : ISftpClientAdapter
{
    // ── internal tree ─────────────────────────────────────────────────────────

    internal sealed class Node
    {
        public bool IsDirectory;
        // False in all base-fake nodes; set explicitly in tests that need a symlink entry.
        public bool IsSymlink = false;
        public byte[] Bytes = [];
        public DateTime LastWriteTimeUtc = DateTime.UnixEpoch;
        // Default permissions: owner rw(x for dirs), group r, others r.
        public bool OwnerRead = true, OwnerWrite = true;
        public bool OwnerExecute; // set to true for dirs by CreateDirectory
        public bool GroupRead = true, GroupWrite = false, GroupExecute = false;
        public bool OtherRead = true, OtherWrite = false, OtherExecute = false;
    }

    // Keyed by normalized full path ("/", "/a", "/a/b.txt"). Root always present.
    private readonly Dictionary<string, Node> _nodes = new()
    {
        ["/"] = new Node { IsDirectory = true, OwnerExecute = true },
    };

    // ── path normalisation ────────────────────────────────────────────────────

    private static string Norm(string path) => path.Length > 1 ? path.TrimEnd('/') : "/";

    private static string NameOf(string normalizedPath) =>
        normalizedPath == "/" ? "" : normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];

    private static string ParentOf(string normalizedPath) =>
        normalizedPath == "/" ? "/" :
        normalizedPath[..normalizedPath.LastIndexOf('/')] is { Length: 0 } ? "/" :
        normalizedPath[..normalizedPath.LastIndexOf('/')];

    private Node Require(string path)
    {
        var n = Norm(path);
        return _nodes.TryGetValue(n, out var node) ? node
               : throw new SftpPathNotFoundException($"No such path: {path}");
    }

    private SftpEntry ToEntry(string normalizedPath, Node node) => new(
        Name: NameOf(normalizedPath),
        FullName: normalizedPath,
        IsDirectory: node.IsDirectory,
        IsSymbolicLink: node.IsSymlink,
        Length: node.IsDirectory ? 0 : node.Bytes.Length,
        LastWriteTimeUtc: node.LastWriteTimeUtc,
        OwnerCanRead: node.OwnerRead,
        OwnerCanWrite: node.OwnerWrite,
        OwnerCanExecute: node.OwnerExecute,
        GroupCanRead: node.GroupRead,
        GroupCanWrite: node.GroupWrite,
        GroupCanExecute: node.GroupExecute,
        OthersCanRead: node.OtherRead,
        OthersCanWrite: node.OtherWrite,
        OthersCanExecute: node.OtherExecute);

    // ── ISftpClientAdapter transport ─────────────────────────────────────────

    private bool _connected;
    public bool IsConnected => _connected;

    /// <summary>
    /// When non-null the next <see cref="Connect"/> call will throw this exception
    /// and then clear the field (one-shot).  Used by reconnect tests.
    /// </summary>
    public Exception? NextConnectThrow { get; set; }

    /// <summary>Total number of successful <see cref="Connect"/> calls (initial + reconnects).</summary>
    public int ConnectCount { get; private set; }

    /// <summary>
    /// When non-null the next <see cref="ListDirectory"/> enumeration will throw this
    /// exception and then clear the field (one-shot).  Used by reconnect-retry tests.
    /// </summary>
    public Exception? NextListThrow { get; set; }

    /// <summary>
    /// Per-path persistent throws for <see cref="ListDirectory"/> — every enumeration of a
    /// listed path throws the mapped exception.  Used by per-directory failure tests.
    /// </summary>
    public Dictionary<string, Exception> ListThrowsByPath { get; } = new();

    public void Connect()
    {
        if (NextConnectThrow is { } ex)
        {
            NextConnectThrow = null;
            throw ex;
        }

        _connected = true;
        ConnectCount++;
    }

    public void Disconnect() => _connected = false;

    public void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler) { /* no-op in fake */ }

    // ── narrow SFTP ops ───────────────────────────────────────────────────────

    public IEnumerable<SftpEntry> ListDirectory(string path)
    {
        // Deferred to first MoveNext, which is fine: the provider materializes the
        // enumeration inside WithReconnect, so the throw lands inside the guarded op.
        if (NextListThrow is { } listEx)
        {
            NextListThrow = null;
            throw listEx;
        }

        if (ListThrowsByPath.TryGetValue(Norm(path), out var pathEx))
            throw pathEx;

        var dir = Norm(path);
        var node = Require(dir);
        if (!node.IsDirectory)
            throw new SftpPathNotFoundException($"Not a directory: {dir}");

        // Emit "." and ".." to mirror a real SFTP server.
        yield return ToEntry(".", node) with { FullName = dir };
        var parentPath = ParentOf(dir);
        yield return ToEntry("..", _nodes.TryGetValue(parentPath, out var pn) ? pn : node) with { FullName = parentPath };

        var prefix = dir == "/" ? "/" : dir + "/";
        foreach (var (k, v) in _nodes)
        {
            if (k == dir) continue;
            if (!k.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = k[prefix.Length..];
            if (rest.Contains('/')) continue; // only immediate children
            yield return ToEntry(k, v);
        }
    }

    public SftpEntry? Get(string path)
    {
        var n = Norm(path);
        return _nodes.TryGetValue(n, out var node) ? ToEntry(n, node) : null;
    }

    public bool IsDirectory(string path)
    {
        var n = Norm(path);
        return _nodes.TryGetValue(n, out var node) && node.IsDirectory;
    }

    public bool IsFile(string path)
    {
        var n = Norm(path);
        return _nodes.TryGetValue(n, out var node) && !node.IsDirectory;
    }

    public void CreateDirectory(string path)
    {
        var n = Norm(path);
        if (_nodes.ContainsKey(n))
            throw new SftpPermissionDeniedException($"Already exists: {n}");
        _nodes[n] = new Node { IsDirectory = true, OwnerExecute = true };
    }

    public void CreateFile(string path)
    {
        var n = Norm(path);
        if (_nodes.ContainsKey(n))
            throw new SftpPermissionDeniedException($"Already exists: {n}");
        _nodes[n] = new Node { IsDirectory = false };
    }

    public void RenameFile(string oldPath, string newPath, bool isPosix = false)
    {
        var from = Norm(oldPath);
        var to = Norm(newPath);

        if (!_nodes.ContainsKey(from))
            throw new SftpPathNotFoundException($"Source not found: {from}");

        // Non-POSIX rename fails if target exists; POSIX-rename replaces it.
        if (!isPosix && _nodes.ContainsKey(to))
            throw new SftpPermissionDeniedException($"Destination exists: {to}");

        // Move the subtree: rename all keys that start with from.
        var toMove = _nodes.Keys
            .Where(k => k == from || k.StartsWith(from + "/", StringComparison.Ordinal))
            .ToList();

        foreach (var key in toMove)
        {
            var newKey = to + key[from.Length..];
            _nodes[newKey] = _nodes[key];
            _nodes.Remove(key);
        }
    }

    public void DeleteFile(string path)
    {
        var n = Norm(path);
        if (!_nodes.Remove(n))
            throw new SftpPathNotFoundException($"File not found: {n}");
    }

    public void DeleteDirectory(string path)
    {
        var n = Norm(path);
        if (!_nodes.TryGetValue(n, out var node) || !node.IsDirectory)
            throw new SftpPathNotFoundException($"Directory not found: {n}");
        _nodes.Remove(n);
    }

    public bool Exists(string path) => _nodes.ContainsKey(Norm(path));

    public Stream OpenRead(string path)
    {
        var node = Require(Norm(path));
        return new MemoryStream(node.Bytes, writable: false);
    }

    public Stream OpenWrite(string path)
    {
        var n = Norm(path);
        if (!_nodes.TryGetValue(n, out var node))
            node = _nodes[n] = new Node();
        return new WriteBackStream(bytes =>
        {
            node.Bytes = bytes;
            node.LastWriteTimeUtc = DateTime.UtcNow;
        });
    }

    public void SetLastWriteTimeUtc(string path, DateTime utc) =>
        Require(Norm(path)).LastWriteTimeUtc = utc;

    public void Dispose() => _connected = false;

    // ── helper: write-back stream ─────────────────────────────────────────────

    private sealed class WriteBackStream(Action<byte[]> commit) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                commit(ToArray());
            base.Dispose(disposing);
        }
    }
}
