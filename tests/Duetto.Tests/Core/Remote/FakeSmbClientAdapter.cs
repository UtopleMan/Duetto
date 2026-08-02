using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

// In-memory SMB tree keyed by provider-local paths ("/share", "/share/dir/file"). Top-level
// directories are the "shares" returned by ListShares. ListDirectory emits "." and ".." to
// mirror a real SMB server; the provider filters them. OpenWrite writes back to the node on
// Dispose.
internal sealed class FakeSmbClientAdapter : ISmbClientAdapter
{
    internal sealed class Node
    {
        public bool IsDirectory;
        public bool IsReadOnly;
        public byte[] Bytes = [];
        public DateTime LastWriteTimeUtc = DateTime.UnixEpoch;
    }

    private readonly Dictionary<string, Node> nodes = new(StringComparer.Ordinal);

    private static string Norm(string path)
    {
        var p = path.Replace('\\', '/');
        if (p.Length == 0 || p[0] != '/')
            p = "/" + p;
        return p.Length > 1 ? p.TrimEnd('/') : "/";
    }

    private static string NameOf(string normalized) =>
        normalized == "/" ? "" : normalized[(normalized.LastIndexOf('/') + 1)..];

    private static string ParentOf(string normalized)
    {
        if (normalized == "/")
            return "/";
        var slash = normalized.LastIndexOf('/');
        return slash <= 0 ? "/" : normalized[..slash];
    }

    private static bool IsShare(string normalized) =>
        normalized.Length > 1 && normalized.IndexOf('/', 1) < 0;

    private SmbEntry ToEntry(string normalized, Node node) => new(
        Name: NameOf(normalized),
        FullName: normalized,
        IsDirectory: node.IsDirectory,
        IsReadOnly: node.IsReadOnly,
        Length: node.IsDirectory ? -1 : node.Bytes.Length,
        LastWriteTimeUtc: node.LastWriteTimeUtc);

    private bool connected;
    public bool IsConnected => connected;

    // One-shot: the next Connect throws this then clears it. Used by reconnect tests.
    public Exception? NextConnectThrow { get; set; }

    public int ConnectCount { get; private set; }

    // One-shot: the next ListDirectory enumeration throws this then clears it.
    public Exception? NextListThrow { get; set; }

    // Persistent: every enumeration of a listed path throws the mapped exception.
    public Dictionary<string, Exception> ListThrowsByPath { get; } = new();

    // Lock-scope tests: signalled on Connect entry; Connect blocks on the gate to simulate a
    // slow handshake.
    public ManualResetEventSlim? ConnectEntered { get; set; }
    public ManualResetEventSlim? ConnectGate { get; set; }

    // Lock-scope tests: signalled on Disconnect/Dispose entry; blocks on the gate to simulate a
    // stalled graceful close.
    public ManualResetEventSlim? DisconnectEntered { get; set; }
    public ManualResetEventSlim? DisconnectGate { get; set; }

    public void Connect()
    {
        ConnectEntered?.Set();
        ConnectGate?.Wait();

        if (NextConnectThrow is { } ex)
        {
            NextConnectThrow = null;
            throw ex;
        }

        connected = true;
        ConnectCount++;
    }

    public void Disconnect()
    {
        DisconnectEntered?.Set();
        DisconnectGate?.Wait();
        connected = false;
    }

    public void Dispose()
    {
        DisconnectEntered?.Set();
        DisconnectGate?.Wait();
        connected = false;
    }

    public IReadOnlyList<string> ListShares() =>
        nodes.Where(kv => IsShare(kv.Key) && kv.Value.IsDirectory)
             .Select(kv => NameOf(kv.Key))
             .ToList();

    public IEnumerable<SmbEntry> ListDirectory(string path)
    {
        if (NextListThrow is { } listEx)
        {
            NextListThrow = null;
            throw listEx;
        }

        var dir = Norm(path);
        if (ListThrowsByPath.TryGetValue(dir, out var pathEx))
            throw pathEx;

        if (!nodes.TryGetValue(dir, out var node) || !node.IsDirectory)
            throw new FileNotFoundException($"Not a directory: {dir}");

        var results = new List<SmbEntry>
        {
            ToEntry(dir, node) with { Name = "." },
            ToEntry(ParentOf(dir), nodes.TryGetValue(ParentOf(dir), out var pn) ? pn : node) with { Name = ".." },
        };

        var prefix = dir + "/";
        foreach (var (key, value) in nodes)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (key[prefix.Length..].Contains('/'))
                continue;
            results.Add(ToEntry(key, value));
        }

        return results;
    }

    public SmbEntry? Get(string path)
    {
        var n = Norm(path);
        return nodes.TryGetValue(n, out var node) ? ToEntry(n, node) : null;
    }

    public bool IsDirectory(string path) => nodes.TryGetValue(Norm(path), out var n) && n.IsDirectory;

    public bool IsFile(string path) => nodes.TryGetValue(Norm(path), out var n) && !n.IsDirectory;

    public bool Exists(string path) => nodes.ContainsKey(Norm(path));

    public void CreateDirectory(string path)
    {
        var n = Norm(path);
        if (nodes.ContainsKey(n))
            throw new IOException($"Already exists: {n}");
        nodes[n] = new Node { IsDirectory = true };
    }

    public void CreateFile(string path)
    {
        var n = Norm(path);
        if (nodes.ContainsKey(n))
            throw new IOException($"Already exists: {n}");
        nodes[n] = new Node { IsDirectory = false };
    }

    public void RenameFile(string oldPath, string newPath, bool replaceExisting)
    {
        var from = Norm(oldPath);
        var to = Norm(newPath);

        if (!nodes.ContainsKey(from))
            throw new FileNotFoundException($"Source not found: {from}");
        if (!replaceExisting && nodes.ContainsKey(to))
            throw new IOException($"Destination exists: {to}");

        var moving = nodes.Keys
            .Where(k => k == from || k.StartsWith(from + "/", StringComparison.Ordinal))
            .ToList();

        foreach (var key in moving)
        {
            var newKey = to + key[from.Length..];
            nodes[newKey] = nodes[key];
            nodes.Remove(key);
        }
    }

    public void DeleteFile(string path)
    {
        if (!nodes.Remove(Norm(path)))
            throw new FileNotFoundException($"File not found: {path}");
    }

    public void DeleteDirectory(string path)
    {
        var n = Norm(path);
        if (!nodes.TryGetValue(n, out var node) || !node.IsDirectory)
            throw new FileNotFoundException($"Directory not found: {n}");
        nodes.Remove(n);
    }

    public Stream OpenRead(string path)
    {
        var n = Norm(path);
        if (!nodes.TryGetValue(n, out var node))
            throw new FileNotFoundException($"File not found: {n}");
        return new MemoryStream(node.Bytes, writable: false);
    }

    public Stream OpenWrite(string path)
    {
        var n = Norm(path);
        if (!nodes.TryGetValue(n, out var node))
            node = nodes[n] = new Node();
        node.IsDirectory = false;
        return new WriteBackStream(bytes =>
        {
            node.Bytes = bytes;
            node.LastWriteTimeUtc = DateTime.UtcNow;
        });
    }

    public void SetLastWriteTimeUtc(string path, DateTime utc)
    {
        var n = Norm(path);
        if (!nodes.TryGetValue(n, out var node))
            throw new FileNotFoundException($"File not found: {n}");
        node.LastWriteTimeUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    // Toggle for engine + provider fallback tests: when false, ServerSideCopy returns false so
    // the caller streams instead.
    public bool ServerSideCopySupported { get; set; } = true;

    public bool ServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token)
    {
        if (!ServerSideCopySupported)
            return false;

        var from = Norm(source);
        var to = Norm(dest);
        if (!nodes.TryGetValue(from, out var srcNode))
            throw new FileNotFoundException($"Source not found: {from}");

        var copy = (byte[])srcNode.Bytes.Clone();
        nodes[to] = new Node { IsDirectory = false, Bytes = copy, LastWriteTimeUtc = DateTime.UtcNow };
        onBytesCopied(copy.Length);
        return true;
    }

    // Test hook: flip the read-only DOS attribute so provider mapping can be exercised.
    public void MarkReadOnly(string path, bool readOnly)
    {
        var n = Norm(path);
        if (!nodes.TryGetValue(n, out var node))
            throw new FileNotFoundException($"File not found: {n}");
        node.IsReadOnly = readOnly;
    }

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

internal sealed class FakeSmbFactory(FakeSmbClientAdapter adapter) : ISmbClientFactory
{
    public ISmbClientAdapter Create(SmbConnectionInfo info, ConnectSecret secret) => adapter;
}
