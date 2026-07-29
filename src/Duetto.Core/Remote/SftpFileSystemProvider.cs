using Duetto.Core.FileSystem;
using Renci.SshNet.Common;

namespace Duetto.Core.Remote;

/// <summary>
/// <see cref="IFileSystemProvider"/> backed by an SFTP session managed by <see cref="SftpConnection"/>.
/// All operations go through <see cref="SftpConnection.WithReconnect{T}"/> for resilience.
///
/// <para>
/// Capabilities: CanRename, CanCreateEmptyDir, CanCreateFile, CanDelete, HasPermissions,
/// PreservesMTime, AtomicRename (POSIX-rename), SupportsSearch = true;
/// HasTrash, CanWatch, ReportsCapacity = false; Separator = '/', CaseSensitive = true.
/// </para>
///
/// <para>
/// <b>Thread safety:</b> <see cref="SftpConnection.WithReconnect{T}"/> is not thread-safe;
/// this provider serialises concurrent calls with a lock so multi-threaded callers are safe.
/// </para>
/// </summary>
public sealed class SftpFileSystemProvider : IFileSystemProvider, IDisposable
{
    private readonly SftpConnection _conn;
    private readonly object _lock = new();

    /// <summary>
    /// Capabilities for an SFTP backend.
    /// AtomicRename = true because POSIX-rename (used by ReplaceFile) is atomic on the server.
    /// HasTrash = false — remote delete is always permanent.
    /// </summary>
    public static readonly FileSystemCapabilities SftpCapabilities = new()
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

    public FileSystemCapabilities Capabilities => SftpCapabilities;

    /// <param name="connection">
    ///   A fully-constructed (not yet necessarily connected) <see cref="SftpConnection"/>.
    ///   The provider connects lazily on first operation via <see cref="SftpConnection.WithReconnect{T}"/>.
    /// </param>
    public SftpFileSystemProvider(SftpConnection connection)
    {
        _conn = connection;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Joins a parent SFTP path and a name using '/' as the separator.
    /// "/home/user" + "foo" → "/home/user/foo"; "/" + "foo" → "/foo".
    /// </summary>
    private static string Join(string parent, string name)
    {
        var p = parent.TrimEnd('/');
        return p.Length == 0 ? "/" + name : p + "/" + name;
    }

    /// <summary>
    /// Extracts the parent directory from an SFTP path, or throws if at root.
    /// "/home/user/foo" → "/home/user"; "/foo" → "/".
    /// </summary>
    private static string ParentOf(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        if (slash < 0)
            throw new ArgumentException($"Cannot determine parent of '{fullPath}'", nameof(fullPath));
        return slash == 0 ? "/" : trimmed[..slash];
    }

    /// <summary>
    /// Maps a <see cref="SftpEntry"/> to a <see cref="FileEntry"/>.
    /// Unix permissions are reconstructed from the individual permission booleans on the entry.
    /// </summary>
    private static FileEntry MapEntry(SftpEntry e)
    {
        var mode = BuildUnixFileMode(e);
        return new FileEntry
        {
            Name = e.Name,
            FullPath = e.FullName,
            IsDirectory = e.IsDirectory,
            SizeBytes = e.IsDirectory ? -1 : e.Length,
            TypeLabel = FormatUtil.TypeLabel(e.Name, e.IsDirectory),
            ModifiedUtc = e.LastWriteTimeUtc,
            UnixPermissions = FormatUtil.UnixPermissions(mode),
            AccessSummary = e.OwnerCanWrite ? "RW" : "R",
        };
    }

    /// <summary>
    /// Reconstructs a <see cref="UnixFileMode"/> from the individual permission booleans
    /// in an <see cref="SftpEntry"/>.
    /// </summary>
    private static UnixFileMode BuildUnixFileMode(SftpEntry e)
    {
        var mode = UnixFileMode.None;
        if (e.OwnerCanRead) mode |= UnixFileMode.UserRead;
        if (e.OwnerCanWrite) mode |= UnixFileMode.UserWrite;
        if (e.OwnerCanExecute) mode |= UnixFileMode.UserExecute;
        if (e.GroupCanRead) mode |= UnixFileMode.GroupRead;
        if (e.GroupCanWrite) mode |= UnixFileMode.GroupWrite;
        if (e.GroupCanExecute) mode |= UnixFileMode.GroupExecute;
        if (e.OthersCanRead) mode |= UnixFileMode.OtherRead;
        if (e.OthersCanWrite) mode |= UnixFileMode.OtherWrite;
        if (e.OthersCanExecute) mode |= UnixFileMode.OtherExecute;
        return mode;
    }

    /// <summary>Wraps an operation with the connection's reconnect guard AND the provider lock.</summary>
    private T Exec<T>(Func<ISftpClientAdapter, T> op)
    {
        lock (_lock)
            return _conn.WithReconnect(() => op(_conn.Adapter));
    }

    private void Exec(Action<ISftpClientAdapter> op)
    {
        lock (_lock)
            _conn.WithReconnect(() => op(_conn.Adapter));
    }

    // ── IFileSystemProvider ───────────────────────────────────────────────────

    public IReadOnlyList<FileEntry> List(string path) =>
        Exec(a => a.ListDirectory(path)
                   .Where(e => e.Name is not ("." or ".."))
                   .Select(MapEntry)
                   .ToList());

    public bool DirectoryExists(string path) =>
        Exec(a => a.IsDirectory(path));

    public bool FileExists(string path) =>
        Exec(a => a.IsFile(path));

    public FileEntry? Stat(string path)
    {
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
        Exec(a => a.RenameFile(fullPath, target));
        return target;
    }

    /// <summary>
    /// Cross-directory move using SFTP rename.  Throws <see cref="IOException"/> when
    /// <paramref name="toPath"/> already exists (SFTP non-POSIX rename does not overwrite).
    /// </summary>
    public void Move(string fromPath, string toPath)
    {
        Exec(a =>
        {
            if (a.Exists(toPath))
                throw new IOException($"Destination already exists: {toPath}");
            a.RenameFile(fromPath, toPath);
        });
    }

    /// <summary>
    /// Atomic overwrite: uses POSIX-rename (<c>RenameFile(old, new, isPosix: true)</c>)
    /// so the replace is atomic on the server and works even when the target does not exist.
    /// </summary>
    public void ReplaceFile(string from, string to) =>
        Exec(a => a.RenameFile(from, to, isPosix: true));

    /// <summary>
    /// Permanent recursive delete. <paramref name="toTrash"/> is ignored (<see cref="FileSystemCapabilities.HasTrash"/> = false).
    /// Directories are deleted depth-first (children then the directory itself).
    /// </summary>
    public void Delete(string path, bool toTrash) =>
        Exec(a => DeleteRecursive(a, path));

    private static void DeleteRecursive(ISftpClientAdapter a, string path)
    {
        var entry = a.Get(path);
        if (entry is null)
            throw new IOException($"Path not found: {path}");

        if (entry.IsDirectory)
        {
            foreach (var child in a.ListDirectory(path))
            {
                if (child.Name is "." or "..")
                    continue;
                DeleteRecursive(a, child.FullName);
            }

            a.DeleteDirectory(path);
        }
        else
        {
            a.DeleteFile(path);
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> for sequential reading.
    /// <para>
    /// <b>Stream lifetime:</b> the returned stream is bound to the connection that was live
    /// at open time; a reconnect does not migrate it — after a connection drop the held
    /// stream fails with a channel error, not a clean <see cref="SshConnectionException"/>.
    /// Callers must treat stream failures as fatal for the operation and retry the whole
    /// operation (re-open the stream), not just the individual read.
    /// </para>
    /// </summary>
    public Stream OpenRead(string path) =>
        Exec(a => a.OpenRead(path));

    /// <summary>
    /// Opens <paramref name="path"/> for writing, creating or truncating the target.
    /// <para>
    /// <b>Stream lifetime:</b> the returned stream is bound to the connection that was live
    /// at open time; a reconnect does not migrate it — after a connection drop the held
    /// stream fails with a channel error, not a clean <see cref="SshConnectionException"/>.
    /// Callers must treat stream failures as fatal for the operation and retry the whole
    /// operation (re-open the stream), not just the individual write.
    /// </para>
    /// </summary>
    public Stream OpenWrite(string path) =>
        Exec(a => a.OpenWrite(path));

    public void SetLastWriteTimeUtc(string path, DateTime utc) =>
        Exec(a => a.SetLastWriteTimeUtc(path, utc));

    /// <summary>
    /// Depth-first walk: yields each entry, then recurses into real directories.
    /// Symlinked directories are NOT recursed (cycle guard, matching
    /// <see cref="LocalFileSystemProvider"/> policy).
    /// Per-directory SFTP failures (<see cref="SftpPermissionDeniedException"/>,
    /// <see cref="SftpPathNotFoundException"/>, and other non-connection
    /// <see cref="SshException"/>s) are swallowed so the walk continues past bad
    /// directories — same approach as <see cref="LocalFileSystemProvider.EnumerateRecursive"/>
    /// swallowing <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>.
    /// A <see cref="SshConnectionException"/> reaching this frame means
    /// <see cref="SftpConnection.WithReconnect{T}"/>'s single reconnect retry already
    /// failed, so it propagates to the caller.
    /// </summary>
    public IEnumerable<FileEntry> EnumerateRecursive(string path)
    {
        List<SftpEntry> children;
        try
        {
            lock (_lock)
                children = _conn.WithReconnect(() =>
                    _conn.Adapter.ListDirectory(path)
                                 .Where(e => e.Name is not ("." or ".."))
                                 .ToList());
        }
        catch (SshException e) when (e is not SshConnectionException and not SshAuthenticationException)
        {
            // Covers SftpPermissionDeniedException, SftpPathNotFoundException, and
            // low-level per-directory SFTP protocol errors: skip this directory.
            // SshConnectionException and SshAuthenticationException propagate so the
            // caller knows about connection/auth failures rather than silently truncating.
            yield break;
        }

        foreach (var child in children)
        {
            yield return MapEntry(child);

            // Recurse into real directories only; skip symlinks to avoid cycles.
            if (child.IsDirectory && !child.IsSymbolicLink)
                foreach (var descendant in EnumerateRecursive(child.FullName))
                    yield return descendant;
        }
    }

    /// <summary>Returns null — <see cref="FileSystemCapabilities.ReportsCapacity"/> is false.</summary>
    public VolumeInfo? VolumeFor(string path) => null;

    public void Dispose() => _conn.Dispose();
}
