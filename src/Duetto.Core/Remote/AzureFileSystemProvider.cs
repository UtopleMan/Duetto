using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

// Maps the provider-local root "/" to the container list (or the single configured container);
// "/container/blob…" addresses blobs. Blob storage has no real directories — a folder is a blob-name
// prefix, and an empty folder is a zero-byte "prefix/" marker blob. Serialises concurrent calls with
// a lock (AzureConnection is not thread-safe) so UI panes and search threads are safe.
public sealed class AzureFileSystemProvider : IFileSystemProvider, IBackendIdentity, IServerSideCopy, IDisposable
{
    private readonly AzureConnection conn;
    private readonly Lock gate = new();

    // CanRename = false: blob stores have no rename, so the transfer engine routes moves through copy
    // + delete — and, when both panes share this connection, through the server-side Copy Blob offload
    // (IServerSideCopy) with no bytes crossing the client. AtomicRename = false: an upload only becomes
    // visible when complete, so no ".part" staging is needed. HasTrash = false: permanent delete.
    // PreservesMTime = false: the service owns the blob's LastModified.
    public static readonly FileSystemCapabilities AzureCapabilities = new()
    {
        CanRename = false,
        CanCreateEmptyDir = true,
        CanCreateFile = true,
        CanDelete = true,
        HasTrash = false,
        HasPermissions = false,
        PreservesMTime = false,
        AtomicRename = false,
        CanWatch = false,
        ReportsCapacity = false,
        SupportsSearch = true,
        CaseSensitive = true,
        Separator = '/',
    };

    public FileSystemCapabilities Capabilities => AzureCapabilities;

    public AzureFileSystemProvider(AzureConnection connection)
    {
        conn = connection;
    }

    private static bool IsRoot(string path) => path is "" or "/";

    // Splits a provider-local path into (container, key). key is "" at a container root.
    private static (string Container, string Key) Split(string path)
    {
        var trimmed = path.Trim('/');
        if (trimmed.Length == 0)
            return ("", "");
        var slash = trimmed.IndexOf('/');
        return slash < 0 ? (trimmed, "") : (trimmed[..slash], trimmed[(slash + 1)..]);
    }

    // The listing prefix for a folder key: "" for a container root, else "key/".
    private static string PrefixFor(string key) => key.Length == 0 ? "" : key.TrimEnd('/') + "/";

    private static string Join(string parent, string name)
    {
        var p = parent.TrimEnd('/');
        return p.Length == 0 ? "/" + name : p + "/" + name;
    }

    private static string ParentOf(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        if (slash < 0)
            throw new ArgumentException($"Cannot determine parent of '{fullPath}'", nameof(fullPath));
        return slash == 0 ? "/" : trimmed[..slash];
    }

    private static FileEntry MapEntry(AzureEntry e) => new()
    {
        Name = e.Name,
        FullPath = e.FullName,
        IsDirectory = e.IsDirectory,
        SizeBytes = e.IsDirectory ? -1 : e.Length,
        TypeLabel = FormatUtil.TypeLabel(e.Name, e.IsDirectory),
        ModifiedUtc = e.LastWriteTimeUtc,
        UnixPermissions = "",
        AccessSummary = e.IsReadOnly ? "R" : "RW",
    };

    private static FileEntry DirEntry(string name, string fullPath) => new()
    {
        Name = name,
        FullPath = fullPath,
        IsDirectory = true,
        SizeBytes = -1,
        TypeLabel = FormatUtil.TypeLabel(name, isDirectory: true),
        ModifiedUtc = default,
        UnixPermissions = "",
        AccessSummary = "RW",
    };

    private T Exec<T>(Func<IAzureClientAdapter, T> op)
    {
        lock (gate)
            return conn.WithReconnect(() => op(conn.Adapter));
    }

    private void Exec(Action<IAzureClientAdapter> op)
    {
        lock (gate)
            conn.WithReconnect(() => op(conn.Adapter));
    }

    private IReadOnlyList<string> Containers(IAzureClientAdapter a) =>
        conn.ConfiguredContainer.Length > 0 ? [conn.ConfiguredContainer] : a.ListContainers();

    public IReadOnlyList<FileEntry> List(string path)
    {
        if (IsRoot(path))
            return Exec(a => Containers(a).Select(c => DirEntry(c, "/" + c)).ToList());

        var (container, key) = Split(path);
        return Exec(a => a.ListBlobs(container, PrefixFor(key)).Select(MapEntry).ToList());
    }

    public bool DirectoryExists(string path)
    {
        if (IsRoot(path))
            return true;

        var (container, key) = Split(path);
        if (key.Length == 0)
            return conn.ConfiguredContainer.Length > 0
                ? string.Equals(container, conn.ConfiguredContainer, StringComparison.Ordinal)
                : Exec(a => a.ListContainers().Contains(container));

        return Exec(a => a.PrefixExists(container, PrefixFor(key)));
    }

    public bool FileExists(string path)
    {
        if (IsRoot(path))
            return false;

        var (container, key) = Split(path);
        return key.Length != 0 && Exec(a => a.StatBlob(container, key) is not null);
    }

    public FileEntry? Stat(string path)
    {
        if (IsRoot(path))
            return DirEntry("", "/");

        var (container, key) = Split(path);
        if (key.Length == 0)
            return DirectoryExists(path) ? DirEntry(container, "/" + container) : null;

        return Exec<FileEntry?>(a =>
        {
            var file = a.StatBlob(container, key);
            if (file is not null)
                return MapEntry(file);
            return a.PrefixExists(container, PrefixFor(key)) ? DirEntry(key.Split('/')[^1], path) : null;
        });
    }

    // Creates a zero-byte "prefix/" marker so an empty folder is visible.
    public string CreateDirectory(string parent, string name)
    {
        var target = Join(parent, name);
        var (container, key) = Split(target);
        if (key.Length == 0)
            throw new IOException("Cannot create a container here; create it in the Azure portal.");
        Exec(a => a.PutEmptyBlob(container, PrefixFor(key)));
        return target;
    }

    public string CreateFile(string parent, string name)
    {
        var target = Join(parent, name);
        var (container, key) = Split(target);
        if (key.Length == 0)
            throw new IOException("Cannot create a file at the container-list root.");
        Exec(a => a.PutEmptyBlob(container, key));
        return target;
    }

    // File rename only. Blob-store folders are name prefixes; renaming one means bulk-copying every
    // child, which is out of scope — the transfer engine handles moving a folder's contents instead.
    public string Rename(string fullPath, string newName)
    {
        var parent = ParentOf(fullPath);
        var target = Join(parent, newName);
        var (container, key) = Split(fullPath);
        var (_, newKey) = Split(target);
        if (key.Length == 0)
            throw new IOException("Cannot rename a container.");

        Exec(a =>
        {
            if (a.StatBlob(container, key) is null)
                throw new NotSupportedException("Renaming an Azure folder is not supported.");
            a.CopyBlob(container, key, container, newKey, _ => { }, CancellationToken.None);
            a.DeleteBlob(container, key);
        });
        return target;
    }

    // File move = server-side Copy Blob + delete. Guards against overwrite (the engine only calls this
    // when the destination is free). Folders are handled by the engine's tree walk.
    public void Move(string fromPath, string toPath)
    {
        var (sc, sk) = Split(fromPath);
        var (dc, dk) = Split(toPath);
        if (sk.Length == 0 || dk.Length == 0)
            throw new IOException("Cannot move a container.");

        Exec(a =>
        {
            if (a.StatBlob(sc, sk) is null)
                throw new NotSupportedException("Moving an Azure folder in place is not supported.");
            if (a.StatBlob(dc, dk) is not null)
                throw new IOException($"Destination already exists: {toPath}");
            if (!a.CopyBlob(sc, sk, dc, dk, _ => { }, CancellationToken.None))
            {
                // No server-side copy available (e.g. SAS/anonymous creds): stream through the client.
                using var src = a.OpenRead(sc, sk);
                using var dst = a.OpenWrite(dc, dk);
                src.CopyTo(dst);
            }
            a.DeleteBlob(sc, sk);
        });
    }

    // Overwriting copy + delete of the source. An Azure upload/copy replaces any existing target blob.
    public void ReplaceFile(string from, string to)
    {
        var (sc, sk) = Split(from);
        var (dc, dk) = Split(to);
        if (sk.Length == 0 || dk.Length == 0)
            throw new IOException("Cannot replace a container.");

        Exec(a =>
        {
            if (!a.CopyBlob(sc, sk, dc, dk, _ => { }, CancellationToken.None))
            {
                using var src = a.OpenRead(sc, sk);
                using var dst = a.OpenWrite(dc, dk);
                src.CopyTo(dst);
            }
            a.DeleteBlob(sc, sk);
        });
    }

    public void Delete(string path, bool toTrash)
    {
        var (container, key) = Split(path);
        if (key.Length == 0)
            throw new IOException("Cannot delete a container.");

        Exec(a =>
        {
            if (a.StatBlob(container, key) is not null)
                a.DeleteBlob(container, key);
            else
                a.DeletePrefix(container, PrefixFor(key));
        });
    }

    // The returned stream is bound to the connection live at open time; a reconnect does not migrate
    // it. Callers treat stream failures as fatal for the operation and re-open.
    public Stream OpenRead(string path)
    {
        var (container, key) = Split(path);
        return Exec(a => a.OpenRead(container, key));
    }

    public Stream OpenWrite(string path)
    {
        var (container, key) = Split(path);
        return Exec(a => a.OpenWrite(container, key));
    }

    public void SetLastWriteTimeUtc(string path, DateTime utc) =>
        throw new NotSupportedException("Azure Blob does not support setting a blob's modification time.");

    // Walks the tree level by level so folder entries (name prefixes) are yielded as well as blobs —
    // the transfer engine needs the directory entries to recreate the folder structure at the
    // destination. A lock is taken per level, not held across yields.
    public IEnumerable<FileEntry> EnumerateRecursive(string path)
    {
        if (IsRoot(path))
        {
            var containers = Exec(a => Containers(a).ToList());
            foreach (var container in containers)
            {
                var entry = DirEntry(container, "/" + container);
                yield return entry;
                foreach (var descendant in EnumerateRecursive(entry.FullPath))
                    yield return descendant;
            }
            yield break;
        }

        var (c, key) = Split(path);
        var children = Exec(a => a.ListBlobs(c, PrefixFor(key)));
        foreach (var child in children)
        {
            yield return MapEntry(child);
            if (child.IsDirectory)
                foreach (var descendant in EnumerateRecursive(child.FullName))
                    yield return descendant;
        }
    }

    public VolumeInfo? VolumeFor(string path) => null;

    // Server-side copy/move domain: every blob path of this connection. Two paths reachable by this
    // connection's client can be Copy Blob'd server-side. The container-list root has no domain. Keyed
    // on the connection id (a single client/creds pair reaches all its containers), so cross-container
    // server-side copy within one connection is allowed; a different connection falls back to
    // streaming, which is always correct.
    public string? BackendKey(string path) =>
        IsRoot(path) ? null : $"azure://{conn.ConnId}";

    // Server-side Copy Blob. The engine gates this on BackendKey equality, so both paths are reachable
    // by this connection's client. Returns false when a server-side copy is unavailable (the credential
    // cannot mint a readable source SAS) so the engine streams instead.
    public bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token)
    {
        var (sc, sk) = Split(source);
        var (dc, dk) = Split(dest);
        if (sk.Length == 0 || dk.Length == 0)
            return false;
        return Exec(a => a.CopyBlob(sc, sk, dc, dk, onBytesCopied, token));
    }

    public void Dispose() => conn.Dispose();
}
