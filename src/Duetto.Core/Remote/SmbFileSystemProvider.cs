using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

// The provider-local root "/" maps to the server's share list; "/share/..." operates inside a
// share's tree. This provider serialises concurrent calls with a lock (SmbConnection is not
// thread-safe) so UI panes and search threads are safe.
public sealed class SmbFileSystemProvider : IFileSystemProvider, IDisposable
{
    private readonly SmbConnection conn;
    private readonly Lock gate = new();

    // HasPermissions = false: SMB exposes DOS attributes / ACLs, not a POSIX mode.
    // AtomicRename = true: SMB2 rename-with-ReplaceIfExists finishes a ".part" with no gap.
    // HasTrash = false: remote delete is always permanent.
    public static readonly FileSystemCapabilities SmbCapabilities = new()
    {
        CanRename = true,
        CanCreateEmptyDir = true,
        CanCreateFile = true,
        CanDelete = true,
        HasTrash = false,
        HasPermissions = false,
        PreservesMTime = true,
        AtomicRename = true,
        CanWatch = false,
        ReportsCapacity = false,
        SupportsSearch = true,
        CaseSensitive = false,
        Separator = '/',
    };

    public FileSystemCapabilities Capabilities => SmbCapabilities;

    public SmbFileSystemProvider(SmbConnection connection)
    {
        conn = connection;
    }

    private static bool IsRoot(string path) => path is "" or "/";

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

    private static FileEntry MapEntry(SmbEntry e) => new()
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

    private T Exec<T>(Func<ISmbClientAdapter, T> op)
    {
        lock (gate)
            return conn.WithReconnect(() => op(conn.Adapter));
    }

    private void Exec(Action<ISmbClientAdapter> op)
    {
        lock (gate)
            conn.WithReconnect(() => op(conn.Adapter));
    }

    public IReadOnlyList<FileEntry> List(string path)
    {
        if (IsRoot(path))
            return Exec(a => a.ListShares().Select(s => DirEntry(s, "/" + s)).ToList());

        return Exec(a => a.ListDirectory(path)
                          .Where(e => e.Name is not ("." or ".."))
                          .Select(MapEntry)
                          .ToList());
    }

    public bool DirectoryExists(string path) =>
        IsRoot(path) || Exec(a => a.IsDirectory(path));

    public bool FileExists(string path) =>
        !IsRoot(path) && Exec(a => a.IsFile(path));

    public FileEntry? Stat(string path)
    {
        if (IsRoot(path))
            return DirEntry("", "/");

        var entry = Exec(a => a.Get(path));
        return entry is null ? null : MapEntry(entry);
    }

    public string CreateDirectory(string parent, string name)
    {
        var target = Join(parent, name);
        Exec(a => a.CreateDirectory(target));
        return target;
    }

    public string CreateFile(string parent, string name)
    {
        var target = Join(parent, name);
        Exec(a => a.CreateFile(target));
        return target;
    }

    public string Rename(string fullPath, string newName)
    {
        var parent = ParentOf(fullPath);
        var target = Join(parent, newName);
        Exec(a => a.RenameFile(fullPath, target, replaceExisting: false));
        return target;
    }

    // SMB rename does not overwrite when ReplaceIfExists is false, so guard the destination.
    public void Move(string fromPath, string toPath)
    {
        Exec(a =>
        {
            if (a.Exists(toPath))
                throw new IOException($"Destination already exists: {toPath}");
            a.RenameFile(fromPath, toPath, replaceExisting: false);
        });
    }

    public void ReplaceFile(string from, string to)
    {
        Exec(a =>
        {
            try
            {
                a.RenameFile(from, to, replaceExisting: true);
            }
            // A server that rejects atomic replace falls back to delete-then-rename; a real
            // connection/auth drop is not swallowed so WithReconnect can react.
            catch (IOException ex) when (ex is not SmbConnectionException and not SmbAuthenticationException)
            {
                if (a.Exists(to))
                    a.DeleteFile(to);
                a.RenameFile(from, to, replaceExisting: false);
            }
        });
    }

    public void Delete(string path, bool toTrash) =>
        Exec(a => DeleteRecursive(a, path));

    private static void DeleteRecursive(ISmbClientAdapter a, string path)
    {
        var entry = a.Get(path);
        if (entry is null)
            throw new FileNotFoundException($"Path not found: {path}");

        if (entry.IsDirectory)
        {
            // Materialise the listing before deleting children so a reconnect mid-delete
            // does not re-enumerate from the top.
            var children = a.ListDirectory(path)
                            .Where(e => e.Name is not ("." or ".."))
                            .ToList();
            foreach (var child in children)
                DeleteRecursive(a, child.FullName);

            a.DeleteDirectory(path);
        }
        else
        {
            a.DeleteFile(path);
        }
    }

    // Stream lifetime mirrors SFTP: the returned stream is bound to the connection live at open
    // time; a reconnect does not migrate it. Callers must treat stream failures as fatal for the
    // operation and re-open, not retry the individual read/write.
    public Stream OpenRead(string path) => Exec(a => a.OpenRead(path));

    public Stream OpenWrite(string path) => Exec(a => a.OpenWrite(path));

    public void SetLastWriteTimeUtc(string path, DateTime utc) =>
        Exec(a => a.SetLastWriteTimeUtc(path, utc));

    // Per-directory low-level failures are swallowed so the walk continues past bad directories
    // (matching SftpFileSystemProvider). A SmbConnectionException / SmbAuthenticationException
    // means the single reconnect retry already failed (or auth is broken), so it propagates
    // rather than silently truncating results.
    public IEnumerable<FileEntry> EnumerateRecursive(string path)
    {
        if (IsRoot(path))
        {
            List<string> shares;
            try
            {
                lock (gate)
                    shares = conn.WithReconnect(() => conn.Adapter.ListShares().ToList());
            }
            catch (IOException e) when (e is not SmbConnectionException and not SmbAuthenticationException)
            {
                yield break;
            }

            foreach (var share in shares)
            {
                var entry = DirEntry(share, "/" + share);
                yield return entry;
                foreach (var descendant in EnumerateRecursive(entry.FullPath))
                    yield return descendant;
            }

            yield break;
        }

        List<SmbEntry> children;
        try
        {
            lock (gate)
                children = conn.WithReconnect(() =>
                    conn.Adapter.ListDirectory(path)
                                .Where(e => e.Name is not ("." or ".."))
                                .ToList());
        }
        catch (IOException e) when (e is not SmbConnectionException and not SmbAuthenticationException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            yield return MapEntry(child);

            if (child.IsDirectory)
                foreach (var descendant in EnumerateRecursive(child.FullName))
                    yield return descendant;
        }
    }

    public VolumeInfo? VolumeFor(string path) => null;

    public void Dispose() => conn.Dispose();
}
