using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

public sealed class S3FileSystemProvider : IFileSystemProvider, IBackendIdentity, IServerSideCopy, IDisposable
{
    private readonly S3Connection conn;
    private readonly Lock gate = new();

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

    private static (string Bucket, string Key) Split(string path)
    {
        var trimmed = path.Trim('/');
        if (trimmed.Length == 0)
            return ("", "");
        var slash = trimmed.IndexOf('/');
        return slash < 0 ? (trimmed, "") : (trimmed[..slash], trimmed[(slash + 1)..]);
    }

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

    public string? BackendKey(string path) =>
        IsRoot(path) ? null : $"s3://{conn.ConnId}";

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
