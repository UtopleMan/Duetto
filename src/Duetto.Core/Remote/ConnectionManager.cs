using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

public sealed class ConnectionManager : IDisposable
{
    private readonly FileSystemRegistry _registry;
    private readonly HostKeyStore _hostKeyStore;
    private readonly ISftpClientFactory _factory;
    private readonly object _lock = new();

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    private sealed class Entry(string Id, SftpConnection Connection, SftpFileSystemProvider Provider) : IDisposable
    {
        public readonly string Id = Id;
        public readonly SftpConnection Connection = Connection;
        public readonly SftpFileSystemProvider Provider = Provider;

        public void Dispose()
        {
            Provider.Dispose();
        }
    }

    public ConnectionManager(
        FileSystemRegistry registry,
        HostKeyStore hostKeyStore,
        ISftpClientFactory? factory = null)
    {
        _registry = registry;
        _hostKeyStore = hostKeyStore;
        _factory = factory ?? new DefaultSftpClientFactory();
    }

    public IReadOnlyCollection<string> ConnectedIds
    {
        get
        {
            lock (_lock)
                return _entries.Keys.ToList().AsReadOnly();
        }
    }

    public bool IsConnected(string id)
    {
        lock (_lock)
            return _entries.TryGetValue(id, out var e) && e.Connection.IsConnected;
    }

    public void Connect(ConnectionInfo info, ConnectSecret secret)
    {
        SftpConnection conn;
        Entry? evicted;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(info.Id, out var old))
            {
                _registry.Unregister("sftp", old.Id);
                _entries.Remove(info.Id);
                evicted = old;
            }
            else
            {
                evicted = null;
            }

            conn = new SftpConnection(info, secret, _factory, _hostKeyStore);
        }

        evicted?.Dispose();

        try
        {
            conn.Connect();
        }
        catch (Exception)
        {
            conn.Dispose();
            throw;
        }

        Entry? racedEntry;
        lock (_lock)
        {
            if (_disposed)
            {
                conn.Dispose();
                throw new ObjectDisposedException(nameof(ConnectionManager));
            }

            if (_entries.TryGetValue(info.Id, out var raced))
            {
                _registry.Unregister("sftp", raced.Id);
                _entries.Remove(info.Id);
                racedEntry = raced;
            }
            else
            {
                racedEntry = null;
            }

            var provider = new SftpFileSystemProvider(conn);
            _entries[info.Id] = new Entry(info.Id, conn, provider);
            _registry.Register("sftp", info.Id, provider);
        }

        racedEntry?.Dispose();
    }

    public void Disconnect(string id)
    {
        Entry? entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out entry))
                return;

            _registry.Unregister("sftp", entry.Id);
            _entries.Remove(id);
        }

        entry.Dispose();
    }

    public void DisposeAll()
    {
        List<Entry> toDispose;
        lock (_lock)
            toDispose = CollectAndClearLocked();

        foreach (var e in toDispose)
            e.Dispose();
    }

    public void Dispose()
    {
        List<Entry> toDispose;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            toDispose = CollectAndClearLocked();
        }

        foreach (var e in toDispose)
            e.Dispose();
    }

    private List<Entry> CollectAndClearLocked()
    {
        var entries = new List<Entry>(_entries.Count);
        foreach (var entry in _entries.Values)
        {
            _registry.Unregister("sftp", entry.Id);
            entries.Add(entry);
        }

        _entries.Clear();
        return entries;
    }
}
