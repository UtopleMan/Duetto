using System.Net;
using System.Net.Sockets;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace Duetto.Core.Remote;

public sealed class DefaultSmbClientFactory : ISmbClientFactory
{
    public ISmbClientAdapter Create(SmbConnectionInfo info, ConnectSecret secret) =>
        new RealSmbClientAdapter(info, secret);
}

// Wraps SMBLibrary's SMB2Client behind ISmbClientAdapter. One tree connection is cached per
// share for the life of the connection. All provider-local paths ("/share/dir/file") are split
// into (share, relative) and the relative part is translated to SMB2 backslash form.
internal sealed class RealSmbClientAdapter(SmbConnectionInfo info, ConnectSecret secret) : ISmbClientAdapter
{
    private SMB2Client? client;
    private readonly Dictionary<string, ISMBFileStore> trees = new(StringComparer.OrdinalIgnoreCase);
    private int chunkSize = 65536;

    public bool IsConnected => client?.IsConnected ?? false;

    public void Connect()
    {
        Cleanup();

        var fresh = new SMB2Client();
        var address = ResolveHost(info.Host);
        if (!fresh.Connect(address, SMBTransportType.DirectTCPTransport))
            throw new SmbConnectionException($"Could not connect to SMB host '{info.Host}'.");

        var status = info.Guest
            ? fresh.Login(string.Empty, "Guest", string.Empty)
            : fresh.Login(info.Domain ?? string.Empty, info.Username, secret.Password ?? string.Empty);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            fresh.Disconnect();
            throw new SmbAuthenticationException($"SMB login failed for '{info.Username}': {status}.");
        }

        client = fresh;

        // MaxReadSize / MaxWriteSize are negotiated during connect; use the smaller as the
        // stream chunk size. Guard against a zero from an odd server.
        var negotiated = (int)Math.Min(fresh.MaxReadSize, fresh.MaxWriteSize);
        chunkSize = negotiated > 0 ? negotiated : 65536;
    }

    public void Disconnect() => Cleanup();

    public void Dispose() => Cleanup();

    private void Cleanup()
    {
        foreach (var tree in trees.Values)
        {
            try
            {
                tree.Disconnect();
            }
            catch (Exception)
            {
                // A dead peer must not block teardown of the other trees / the socket.
            }
        }

        trees.Clear();

        if (client is not null)
        {
            try
            {
                if (client.IsConnected)
                    client.Logoff();
            }
            catch (Exception)
            {
            }

            try
            {
                client.Disconnect();
            }
            catch (Exception)
            {
            }
        }

        client = null;
    }

    public IReadOnlyList<string> ListShares() => Run(() =>
    {
        var shares = client!.ListShares(out var status);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, "list shares");
        return (IReadOnlyList<string>)(shares ?? []);
    });

    public IEnumerable<SmbEntry> ListDirectory(string path) => Run(() =>
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.GENERIC_READ, FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"open directory '{path}'");

        try
        {
            var query = store.QueryDirectory(out List<QueryDirectoryFileInformation> list, handle, "*",
                FileInformationClass.FileDirectoryInformation);
            if (query != NTStatus.STATUS_SUCCESS && query != NTStatus.STATUS_NO_MORE_FILES)
                throw Translate(query, $"list directory '{path}'");

            // "." and ".." are returned raw here (mirroring the real server); the provider
            // filters them, matching SftpFileSystemProvider.
            var result = new List<SmbEntry>(list.Count);
            foreach (var item in list)
                result.Add(MapListing(path, (FileDirectoryInformation)item));

            return (IEnumerable<SmbEntry>)result;
        }
        finally
        {
            store.CloseFile(handle);
        }
    });

    public SmbEntry? Get(string path) => Run(() =>
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        // A share root always exists as a directory once the tree connects.
        if (rel.Length == 0)
            return new SmbEntry(share, "/" + share, IsDirectory: true, IsReadOnly: false, Length: -1, LastWriteTimeUtc: default);

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.GENERIC_READ, FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (IsNotFound(status))
            return (SmbEntry?)null;
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"stat '{path}'");

        try
        {
            var got = store.GetFileInformation(out FileInformation fileInfo, handle,
                FileInformationClass.FileNetworkOpenInformation);
            if (got != NTStatus.STATUS_SUCCESS)
                throw Translate(got, $"stat '{path}'");

            var open = (FileNetworkOpenInformation)fileInfo;
            return new SmbEntry(
                Name: LeafOf(path),
                FullName: path,
                IsDirectory: open.IsDirectory,
                IsReadOnly: (open.FileAttributes & FileAttributes.ReadOnly) != 0,
                Length: open.IsDirectory ? -1 : open.EndOfFile,
                LastWriteTimeUtc: open.LastWriteTime.HasValue ? AsUtc(open.LastWriteTime.Value) : default);
        }
        finally
        {
            store.CloseFile(handle);
        }
    });

    public bool IsDirectory(string path) => Get(path) is { IsDirectory: true };

    public bool IsFile(string path) => Get(path) is { IsDirectory: false };

    public bool Exists(string path) => Get(path) is not null;

    public void CreateDirectory(string path) => Run(() =>
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_CREATE,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"create directory '{path}'");
        store.CloseFile(handle);
    });

    public void CreateFile(string path) => Run(() =>
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
            ShareAccess.None, CreateDisposition.FILE_CREATE,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"create file '{path}'");
        store.CloseFile(handle);
    });

    public void RenameFile(string oldPath, string newPath, bool replaceExisting) => Run(() =>
    {
        var (oldShare, oldRel) = Split(oldPath);
        var (newShare, newRel) = Split(newPath);
        if (!string.Equals(oldShare, newShare, StringComparison.OrdinalIgnoreCase))
            throw new IOException("SMB rename across shares is not supported.");

        var store = Tree(oldShare);

        var status = store.CreateFile(out var handle, out _, oldRel,
            AccessMask.DELETE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"open '{oldPath}' for rename");

        try
        {
            var rename = new FileRenameInformationType2
            {
                ReplaceIfExists = replaceExisting,
                FileName = newRel,
            };
            var renamed = store.SetFileInformation(handle, rename);
            if (renamed != NTStatus.STATUS_SUCCESS)
                throw Translate(renamed, $"rename '{oldPath}' to '{newPath}'");
        }
        finally
        {
            store.CloseFile(handle);
        }
    });

    public void DeleteFile(string path) => Run(() => Delete(path, isDirectory: false));

    public void DeleteDirectory(string path) => Run(() => Delete(path, isDirectory: true));

    private void Delete(string path, bool isDirectory)
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        var options = (isDirectory ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE)
                      | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT
                      | CreateOptions.FILE_DELETE_ON_CLOSE;

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            isDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete, CreateDisposition.FILE_OPEN,
            options, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"delete '{path}'");

        // FILE_DELETE_ON_CLOSE removes on close; also flag disposition for servers that need it.
        store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
        store.CloseFile(handle);
    }

    public Stream OpenRead(string path) => Run(() =>
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
            ShareAccess.Read, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"open '{path}' for reading");

        return (Stream)SmbFileStream.ForRead(
            readAt: (offset, count) =>
            {
                var read = store.ReadFile(out var data, handle, offset, Math.Min(count, chunkSize));
                if (read == NTStatus.STATUS_END_OF_FILE || data is null)
                    return [];
                if (read != NTStatus.STATUS_SUCCESS)
                    throw Translate(read, $"read '{path}'");
                return data;
            },
            onClose: () => CloseQuietly(store, handle),
            chunk: chunkSize);
    });

    public Stream OpenWrite(string path) => Run(() =>
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
            ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"open '{path}' for writing");

        return (Stream)SmbFileStream.ForWrite(
            writeAt: (offset, data) =>
            {
                var written = store.WriteFile(out _, handle, offset, data);
                if (written != NTStatus.STATUS_SUCCESS)
                    throw Translate(written, $"write '{path}'");
            },
            onClose: () => CloseQuietly(store, handle),
            chunk: chunkSize);
    });

    public void SetLastWriteTimeUtc(string path, DateTime utc) => Run(() =>
    {
        var (share, rel) = Split(path);
        var store = Tree(share);

        var status = store.CreateFile(out var handle, out _, rel,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"open '{path}' to set mtime");

        try
        {
            // Only LastWriteTime is changed; the other time fields are marked "must not change"
            // and FileAttributes=0 means "leave attributes untouched" (MS-FSCC).
            var basic = new FileBasicInformation
            {
                CreationTime = new SetFileTime(mustNotChange: true),
                LastAccessTime = new SetFileTime(mustNotChange: true),
                ChangeTime = new SetFileTime(mustNotChange: true),
                LastWriteTime = new SetFileTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
                FileAttributes = 0,
            };
            var set = store.SetFileInformation(handle, basic);
            if (set != NTStatus.STATUS_SUCCESS)
                throw Translate(set, $"set mtime '{path}'");
        }
        finally
        {
            store.CloseFile(handle);
        }
    });

    private ISMBFileStore Tree(string share)
    {
        if (trees.TryGetValue(share, out var cached))
            return cached;

        var store = client!.TreeConnect(share, out var status);
        if (status != NTStatus.STATUS_SUCCESS)
            throw Translate(status, $"connect to share '{share}'");

        trees[share] = store;
        return store;
    }

    // A pre-op !IsConnected, or SMBLibrary's InvalidOperationException on a dropped socket, is
    // surfaced as SmbConnectionException so SmbConnection.WithReconnect reconnects once.
    private T Run<T>(Func<T> op)
    {
        if (!IsConnected)
            throw new SmbConnectionException("SMB client is not connected.");

        try
        {
            return op();
        }
        catch (InvalidOperationException ex)
        {
            throw new SmbConnectionException(ex.Message, ex);
        }
        catch (SocketException ex)
        {
            throw new SmbConnectionException(ex.Message, ex);
        }
    }

    private void Run(Action op) => Run(() => { op(); return 0; });

    private static void CloseQuietly(ISMBFileStore store, object handle)
    {
        try
        {
            store.CloseFile(handle);
        }
        catch (Exception)
        {
            // The stream is already being torn down; a late close failure is not actionable.
        }
    }

    private static SmbEntry MapListing(string parentPath, FileDirectoryInformation entry)
    {
        var isDir = (entry.FileAttributes & FileAttributes.Directory) != 0;
        return new SmbEntry(
            Name: entry.FileName,
            FullName: CombineProviderPath(parentPath, entry.FileName),
            IsDirectory: isDir,
            IsReadOnly: (entry.FileAttributes & FileAttributes.ReadOnly) != 0,
            Length: isDir ? -1 : entry.EndOfFile,
            LastWriteTimeUtc: AsUtc(entry.LastWriteTime));
    }

    private static (string Share, string Rel) Split(string path)
    {
        var trimmed = path.Trim('/');
        if (trimmed.Length == 0)
            throw new ArgumentException($"SMB path '{path}' has no share component.", nameof(path));

        var slash = trimmed.IndexOf('/');
        return slash < 0
            ? (trimmed, string.Empty)
            : (trimmed[..slash], trimmed[(slash + 1)..].Replace('/', '\\'));
    }

    private static string CombineProviderPath(string parent, string name)
    {
        var trimmed = parent.TrimEnd('/');
        return trimmed.Length == 0 ? "/" + name : trimmed + "/" + name;
    }

    private static string LeafOf(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static IPAddress ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var literal))
            return literal;

        var addresses = Dns.GetHostAddresses(host);
        var v4 = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);
        if (v4 is not null)
            return v4;
        if (addresses.Length > 0)
            return addresses[0];

        throw new SmbConnectionException($"Could not resolve SMB host '{host}'.");
    }

    private static bool IsNotFound(NTStatus status) => status is
        NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or
        NTStatus.STATUS_OBJECT_PATH_NOT_FOUND or
        NTStatus.STATUS_NO_SUCH_FILE or
        NTStatus.STATUS_NOT_FOUND;

    private static IOException Translate(NTStatus status, string action) =>
        IsNotFound(status)
            ? new FileNotFoundException($"SMB {action}: not found ({status}).")
            : new IOException($"SMB {action} failed: {status}.");
}
