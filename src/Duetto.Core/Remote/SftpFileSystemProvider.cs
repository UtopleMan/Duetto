using Duetto.Core.FileSystem;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Duetto.Core.Remote;

public sealed class SftpFileSystemProvider : IFileSystemProvider, IDisposable
{
    private readonly SftpConnection _conn;
    private readonly object _lock = new();

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

    private FileSystemCapabilities _capabilities = SftpCapabilities;

    public FileSystemCapabilities Capabilities => _capabilities;

    private bool _posixRenameWorked = true;

    public SftpFileSystemProvider(SftpConnection connection)
    {
        _conn = connection;
    }

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

    public void Move(string fromPath, string toPath)
    {
        Exec(a =>
        {
            if (a.Exists(toPath))
                throw new IOException($"Destination already exists: {toPath}");
            a.RenameFile(fromPath, toPath);
        });
    }

    public void ReplaceFile(string from, string to)
    {
        Exec(a =>
        {
            if (_posixRenameWorked)
            {
                try
                {
                    a.RenameFile(from, to, isPosix: true);
                    return;
                }
                catch (SftpException ex) when (ex.StatusCode == StatusCode.OperationUnsupported)
                {
                    _posixRenameWorked = false;
                    _capabilities = _capabilities with { AtomicRename = false };
                }
            }

            if (a.Exists(to))
                a.DeleteFile(to);
            a.RenameFile(from, to);
        });
    }

    public void Delete(string path, bool toTrash) =>
        Exec(a => DeleteRecursive(a, path));

    private static void DeleteRecursive(ISftpClientAdapter a, string path)
    {
        var entry = a.Get(path);
        if (entry is null)
            throw new IOException($"Path not found: {path}");

        if (entry.IsDirectory)
        {
            var children = a.ListDirectory(path).ToList();
            foreach (var child in children)
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

    public Stream OpenRead(string path) =>
        Exec(a => a.OpenRead(path));

    public Stream OpenWrite(string path) =>
        Exec(a => a.OpenWrite(path));

    public void SetLastWriteTimeUtc(string path, DateTime utc) =>
        Exec(a => a.SetLastWriteTimeUtc(path, utc));

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
            yield break;
        }

        foreach (var child in children)
        {
            yield return MapEntry(child);

            if (child.IsDirectory && !child.IsSymbolicLink)
                foreach (var descendant in EnumerateRecursive(child.FullName))
                    yield return descendant;
        }
    }

    public VolumeInfo? VolumeFor(string path) => null;

    public void Dispose() => _conn.Dispose();
}
