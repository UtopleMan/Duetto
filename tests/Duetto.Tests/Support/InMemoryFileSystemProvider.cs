using Duetto.Core.FileSystem;

namespace Duetto.Tests.Support;

/// <summary>
/// A '/'-rooted in-memory <see cref="IFileSystemProvider"/> test double. Mimics a remote
/// backend (no trash, no watch, no capacity) so capability-gating and cross-provider
/// transfers can be exercised without a network. Reused by the registry, UI and transfer
/// tests, and validated against <c>FileSystemProviderContract</c>.
/// </summary>
public sealed class InMemoryFileSystemProvider : IFileSystemProvider
{
    private sealed class Node
    {
        public bool IsDirectory;
        public byte[] Bytes = [];
        public DateTime ModifiedUtc = DateTime.UnixEpoch;
    }

    // Keyed by normalized full path ("/", "/a", "/a/b.txt"). Root always present.
    private readonly Dictionary<string, Node> _nodes = new() { ["/"] = new Node { IsDirectory = true } };

    public FileSystemCapabilities Capabilities { get; init; } = new()
    {
        CanRename = true,
        CanCreateEmptyDir = true,
        CanCreateFile = true,
        CanDelete = true,
        HasTrash = false,
        HasPermissions = true,
        PreservesMTime = true,
        AtomicRename = true,
        CanWatch = false,
        ReportsCapacity = false,
        SupportsSearch = true,
        CaseSensitive = true,
        Separator = '/',
    };

    private static string Norm(string path) => path.Length > 1 ? path.TrimEnd('/') : "/";
    private static string Join(string parent, string name) => parent == "/" ? "/" + name : parent + "/" + name;
    private static string Leaf(string path) => path == "/" ? "" : path[(path.LastIndexOf('/') + 1)..];

    public IReadOnlyList<FileEntry> List(string path)
    {
        var dir = Norm(path);
        if (!_nodes.TryGetValue(dir, out var node) || !node.IsDirectory)
            throw new DirectoryNotFoundException(dir);

        var prefix = dir == "/" ? "/" : dir + "/";
        return _nodes.Keys
            .Where(k => k != dir && k.StartsWith(prefix, StringComparison.Ordinal)
                        && !k[prefix.Length..].Contains('/'))
            .Select(k => ToEntry(k, _nodes[k]))
            .ToList();
    }

    public bool DirectoryExists(string path) => _nodes.TryGetValue(Norm(path), out var n) && n.IsDirectory;
    public bool FileExists(string path) => _nodes.TryGetValue(Norm(path), out var n) && !n.IsDirectory;

    public FileEntry? Stat(string path) =>
        _nodes.TryGetValue(Norm(path), out var n) ? ToEntry(Norm(path), n) : null;

    public string CreateDirectory(string parent, string name)
    {
        var full = Join(Norm(parent), name);
        if (_nodes.ContainsKey(full))
            throw new IOException($"\"{name}\" already exists");
        _nodes[full] = new Node { IsDirectory = true };
        return full;
    }

    public string CreateFile(string parent, string name)
    {
        var full = Join(Norm(parent), name);
        if (_nodes.ContainsKey(full))
            throw new IOException($"\"{name}\" already exists");
        _nodes[full] = new Node { IsDirectory = false };
        return full;
    }

    public string Rename(string fullPath, string newName)
    {
        var from = Norm(fullPath);
        var target = Join(from[..from.LastIndexOf('/')] is { Length: > 0 } p ? p : "/", newName);
        foreach (var key in _nodes.Keys.Where(k => k == from || k.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
        {
            var moved = target + key[from.Length..];
            _nodes[moved] = _nodes[key];
            _nodes.Remove(key);
        }

        return target;
    }

    public void Delete(string path, bool toTrash)
    {
        var target = Norm(path);
        foreach (var key in _nodes.Keys.Where(k => k == target || k.StartsWith(target + "/", StringComparison.Ordinal)).ToList())
            _nodes.Remove(key);
    }

    public void ReplaceFile(string from, string to)
    {
        var source = Norm(from);
        var target = Norm(to);
        // Single dictionary re-key: the old target (if any) is replaced in one step,
        // mirroring the atomic same-directory rename of a real backend.
        _nodes[target] = _nodes[source];
        _nodes.Remove(source);
    }

    public Stream OpenRead(string path) => new MemoryStream(_nodes[Norm(path)].Bytes, writable: false);

    public Stream OpenWrite(string path)
    {
        var full = Norm(path);
        var node = _nodes.TryGetValue(full, out var existing) ? existing : _nodes[full] = new Node();
        return new WriteBackStream(bytes => node.Bytes = bytes);
    }

    public void SetLastWriteTimeUtc(string path, DateTime utc) => _nodes[Norm(path)].ModifiedUtc = utc;

    public IEnumerable<FileEntry> EnumerateRecursive(string path)
    {
        foreach (var entry in List(path))
        {
            yield return entry;
            if (entry.IsDirectory)
                foreach (var child in EnumerateRecursive(entry.FullPath))
                    yield return child;
        }
    }

    public VolumeInfo? VolumeFor(string path) => null;

    private static FileEntry ToEntry(string full, Node n) => new()
    {
        Name = Leaf(full),
        FullPath = full,
        IsDirectory = n.IsDirectory,
        SizeBytes = n.IsDirectory ? -1 : n.Bytes.Length,
        TypeLabel = n.IsDirectory ? "Folder" : "File",
        ModifiedUtc = n.ModifiedUtc,
        UnixPermissions = "",
        AccessSummary = "RW",
    };

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
