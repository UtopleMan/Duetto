using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

public sealed class AzureFileSystemProvider : IFileSystemProvider, IBackendIdentity, IServerSideCopy, IDisposable
{
    private readonly AzureConnection conn;
    private readonly Lock gate = new();

    private const string KeepMarker = ".duettokeep";

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

    private static (string Container, string Key) Split(string path)
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
        return Exec(a => a.ListBlobs(container, PrefixFor(key))
            .Where(e => e.Name != KeepMarker)
            .Select(MapEntry).ToList());
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

    public string CreateDirectory(string parent, string name)
    {
        var target = Join(parent, name);
        var (container, key) = Split(target);
        if (key.Length == 0)
            throw new IOException("Cannot create a container here; create it in the Azure portal.");
        Exec(a => a.PutEmptyBlob(container, PrefixFor(key) + KeepMarker));
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
                using var src = a.OpenRead(sc, sk);
                using var dst = a.OpenWrite(dc, dk);
                src.CopyTo(dst);
            }
            a.DeleteBlob(sc, sk);
        });
    }

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
            if (child.Name == KeepMarker)
                continue;
            yield return MapEntry(child);
            if (child.IsDirectory)
                foreach (var descendant in EnumerateRecursive(child.FullName))
                    yield return descendant;
        }
    }

    public VolumeInfo? VolumeFor(string path) => null;

    public string? BackendKey(string path) =>
        IsRoot(path) ? null : $"azure://{conn.ConnId}";

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
