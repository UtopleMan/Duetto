using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

// Maps the provider-local root "/" to the bucket list (or the single configured bucket);
// "/bucket/key…" addresses objects. S3 has no real directories — a folder is a key prefix, and an
// empty folder is a zero-byte "prefix/" marker object. Serialises concurrent calls with a lock
// (S3Connection is not thread-safe) so UI panes and search threads are safe.
public sealed class S3FileSystemProvider : IFileSystemProvider, IBackendIdentity, IServerSideCopy, IDisposable
{
    private readonly S3Connection conn;
    private readonly Lock gate = new();

    // CanRename = false: object stores have no rename, so the transfer engine routes moves through
    // copy + delete — and, when both panes share this connection, through the server-side CopyObject
    // offload (IServerSideCopy) with no bytes crossing the client. AtomicRename = false: a PUT only
    // becomes visible when complete, so no ".part" staging is needed. HasTrash = false: permanent
    // delete. PreservesMTime = false: S3 owns the object's LastModified.
    public static readonly FileSystemCapabilities S3Capabilities = new()
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

    public FileSystemCapabilities Capabilities => S3Capabilities;

    public S3FileSystemProvider(S3Connection connection)
    {
        conn = connection;
    }

    private static bool IsRoot(string path) => path is "" or "/";

    // Splits a provider-local path into (bucket, key). key is "" at a bucket root.
    private static (string Bucket, string Key) Split(string path)
    {
        var trimmed = path.Trim('/');
        if (trimmed.Length == 0)
            return ("", "");
        var slash = trimmed.IndexOf('/');
        return slash < 0 ? (trimmed, "") : (trimmed[..slash], trimmed[(slash + 1)..]);
    }

    // The listing prefix for a folder key: "" for a bucket root, else "key/".
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

    private static FileEntry MapEntry(S3Entry e) => new()
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

    private T Exec<T>(Func<IS3ClientAdapter, T> op)
    {
        lock (gate)
            return conn.WithReconnect(() => op(conn.Adapter));
    }

    private void Exec(Action<IS3ClientAdapter> op)
    {
        lock (gate)
            conn.WithReconnect(() => op(conn.Adapter));
    }

    private IReadOnlyList<string> Buckets(IS3ClientAdapter a) =>
        conn.ConfiguredBucket.Length > 0 ? [conn.ConfiguredBucket] : a.ListBuckets();

    public IReadOnlyList<FileEntry> List(string path)
    {
        if (IsRoot(path))
            return Exec(a => Buckets(a).Select(b => DirEntry(b, "/" + b)).ToList());

        var (bucket, key) = Split(path);
        return Exec(a => a.ListObjects(bucket, PrefixFor(key)).Select(MapEntry).ToList());
    }

    public bool DirectoryExists(string path)
    {
        if (IsRoot(path))
            return true;

        var (bucket, key) = Split(path);
        if (key.Length == 0)
            return conn.ConfiguredBucket.Length > 0
                ? string.Equals(bucket, conn.ConfiguredBucket, StringComparison.Ordinal)
                : Exec(a => a.ListBuckets().Contains(bucket));

        return Exec(a => a.PrefixExists(bucket, PrefixFor(key)));
    }

    public bool FileExists(string path)
    {
        if (IsRoot(path))
            return false;

        var (bucket, key) = Split(path);
        return key.Length != 0 && Exec(a => a.StatObject(bucket, key) is not null);
    }

    public FileEntry? Stat(string path)
    {
        if (IsRoot(path))
            return DirEntry("", "/");

        var (bucket, key) = Split(path);
        if (key.Length == 0)
            return DirectoryExists(path) ? DirEntry(bucket, "/" + bucket) : null;

        return Exec<FileEntry?>(a =>
        {
            var file = a.StatObject(bucket, key);
            if (file is not null)
                return MapEntry(file);
            return a.PrefixExists(bucket, PrefixFor(key)) ? DirEntry(key.Split('/')[^1], path) : null;
        });
    }

    // Creates a zero-byte "prefix/" marker so an empty folder is visible.
    public string CreateDirectory(string parent, string name)
    {
        var target = Join(parent, name);
        var (bucket, key) = Split(target);
        if (key.Length == 0)
            throw new IOException("Cannot create a bucket here; create it in the S3 console.");
        Exec(a => a.PutEmptyObject(bucket, PrefixFor(key)));
        return target;
    }

    public string CreateFile(string parent, string name)
    {
        var target = Join(parent, name);
        var (bucket, key) = Split(target);
        if (key.Length == 0)
            throw new IOException("Cannot create a file at the bucket-list root.");
        Exec(a => a.PutEmptyObject(bucket, key));
        return target;
    }

    // File rename only. Object-store folders are key prefixes; renaming one means bulk-copying every
    // child, which is out of scope — the transfer engine handles moving a folder's contents instead.
    public string Rename(string fullPath, string newName)
    {
        var parent = ParentOf(fullPath);
        var target = Join(parent, newName);
        var (bucket, key) = Split(fullPath);
        var (_, newKey) = Split(target);
        if (key.Length == 0)
            throw new IOException("Cannot rename a bucket.");

        Exec(a =>
        {
            if (a.StatObject(bucket, key) is null)
                throw new NotSupportedException("Renaming an S3 folder is not supported.");
            a.CopyObject(bucket, key, bucket, newKey, _ => { }, CancellationToken.None);
            a.DeleteObject(bucket, key);
        });
        return target;
    }

    // File move = server-side CopyObject + delete. Guards against overwrite (the engine only calls
    // this when the destination is free). Folders are handled by the engine's tree walk.
    public void Move(string fromPath, string toPath)
    {
        var (sb, sk) = Split(fromPath);
        var (db, dk) = Split(toPath);
        if (sk.Length == 0 || dk.Length == 0)
            throw new IOException("Cannot move a bucket.");

        Exec(a =>
        {
            if (a.StatObject(sb, sk) is null)
                throw new NotSupportedException("Moving an S3 folder in place is not supported.");
            if (a.StatObject(db, dk) is not null)
                throw new IOException($"Destination already exists: {toPath}");
            a.CopyObject(sb, sk, db, dk, _ => { }, CancellationToken.None);
            a.DeleteObject(sb, sk);
        });
    }

    // Overwriting copy + delete of the source. S3 PUT/COPY replaces any existing target object.
    public void ReplaceFile(string from, string to)
    {
        var (sb, sk) = Split(from);
        var (db, dk) = Split(to);
        if (sk.Length == 0 || dk.Length == 0)
            throw new IOException("Cannot replace a bucket.");

        Exec(a =>
        {
            a.CopyObject(sb, sk, db, dk, _ => { }, CancellationToken.None);
            a.DeleteObject(sb, sk);
        });
    }

    public void Delete(string path, bool toTrash)
    {
        var (bucket, key) = Split(path);
        if (key.Length == 0)
            throw new IOException("Cannot delete a bucket.");

        Exec(a =>
        {
            if (a.StatObject(bucket, key) is not null)
                a.DeleteObject(bucket, key);
            else
                a.DeletePrefix(bucket, PrefixFor(key));
        });
    }

    // The returned stream is bound to the connection live at open time; a reconnect does not migrate
    // it. Callers treat stream failures as fatal for the operation and re-open.
    public Stream OpenRead(string path)
    {
        var (bucket, key) = Split(path);
        return Exec(a => a.OpenRead(bucket, key));
    }

    public Stream OpenWrite(string path)
    {
        var (bucket, key) = Split(path);
        return Exec(a => a.OpenWrite(bucket, key));
    }

    public void SetLastWriteTimeUtc(string path, DateTime utc) =>
        throw new NotSupportedException("S3 does not support setting an object's modification time.");

    // Walks the tree level by level so folder entries (key prefixes) are yielded as well as files —
    // the transfer engine needs the directory entries to recreate the folder structure at the
    // destination. A lock is taken per level, not held across yields.
    public IEnumerable<FileEntry> EnumerateRecursive(string path)
    {
        if (IsRoot(path))
        {
            var buckets = Exec(a => Buckets(a).ToList());
            foreach (var bucket in buckets)
            {
                var entry = DirEntry(bucket, "/" + bucket);
                yield return entry;
                foreach (var descendant in EnumerateRecursive(entry.FullPath))
                    yield return descendant;
            }
            yield break;
        }

        var (b, key) = Split(path);
        var children = Exec(a => a.ListObjects(b, PrefixFor(key)));
        foreach (var child in children)
        {
            yield return MapEntry(child);
            if (child.IsDirectory)
                foreach (var descendant in EnumerateRecursive(child.FullName))
                    yield return descendant;
        }
    }

    public VolumeInfo? VolumeFor(string path) => null;

    // Server-side copy/move domain: every object path of this connection. Two paths with equal,
    // non-null keys can be CopyObject'd server-side. The bucket-list root has no domain. Keyed on the
    // connection id (a single client/creds pair reaches all its buckets), so cross-bucket
    // server-side copy within one connection is allowed; a different connection falls back to
    // streaming, which is always correct.
    public string? BackendKey(string path) =>
        IsRoot(path) ? null : $"s3://{conn.ConnId}";

    // Server-side CopyObject. The engine gates this on BackendKey equality, so both paths are
    // reachable by this connection's client. Returns false when CopyObject is unavailable (object
    // too large for a single-part copy) so the engine streams instead.
    public bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token)
    {
        var (sb, sk) = Split(source);
        var (db, dk) = Split(dest);
        if (sk.Length == 0 || dk.Length == 0)
            return false;
        return Exec(a => a.CopyObject(sb, sk, db, dk, onBytesCopied, token));
    }

    public void Dispose() => conn.Dispose();
}
