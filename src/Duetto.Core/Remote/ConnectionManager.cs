using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

public sealed class ConnectionManager : IDisposable
{
    private readonly FileSystemRegistry _registry;
    private readonly HostKeyStore _hostKeyStore;
    private readonly ISftpClientFactory _factory;
    private readonly object _lock = new();

    // Lookups are case-insensitive; Entry.Id preserves the exact casing the provider was
    // registered under (the registry keys are case-sensitive, so unregistering must use the
    // stored casing, never the caller's).
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    private sealed class Entry(string Id, SftpConnection Connection, SftpFileSystemProvider Provider) : IDisposable
    {
        // The id casing used when registering with the (case-sensitive) registry.
        public readonly string Id = Id;
        public readonly SftpConnection Connection = Connection;
        public readonly SftpFileSystemProvider Provider = Provider;

        public void Dispose()
        {
            Provider.Dispose();   // disposes the connection via SftpFileSystemProvider.Dispose
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

    // The SSH handshake can take seconds or hang, so it runs OUTSIDE the manager lock —
    // concurrent IsConnected / ConnectedIds / Disconnect / Dispose calls stay responsive during
    // a slow connect. Races during the unlocked window resolve last-writer-wins.
    public void Connect(ConnectionInfo info, ConnectSecret secret)
    {
        SftpConnection conn;
        Entry? evicted;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Evict any existing connection for this id (replace-on-reconnect).
            // Unregister with the STORED casing — the registry is case-sensitive.
            // Collect the evicted entry so we can dispose it OUTSIDE the lock (a graceful
            // network disconnect inside the lock would stall concurrent state queries).
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

        // Dispose the evicted entry outside the lock so a slow/dead peer cannot stall
        // concurrent IsConnected / ConnectedIds / Connect calls on other threads.
        evicted?.Dispose();

        // Handshake outside the lock: only this thread references `conn` until it is
        // published below, so the non-thread-safe SftpConnection is not shared yet.
        // If this throws, nothing has been registered or tracked.
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
            // The manager may have been disposed while the handshake ran.
            if (_disposed)
            {
                conn.Dispose();
                throw new ObjectDisposedException(nameof(ConnectionManager));
            }

            // A concurrent Connect for the same id may have published meanwhile:
            // last writer wins — evict the raced entry before publishing ours.
            // (A concurrent Disconnect just means no entry exists; we register normally.)
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

        // Dispose the raced entry outside the lock (same reasoning as the pre-handshake eviction).
        racedEntry?.Dispose();
    }

    // Entry.Dispose (which performs a graceful network disconnect) is called AFTER releasing the
    // lock so that a slow or stalled peer cannot freeze IsConnected / ConnectedIds on other threads.
    public void Disconnect(string id)
    {
        Entry? entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out entry))
                return;

            // Unregister with the STORED casing — the registry is case-sensitive and the
            // caller's id may differ in case (manager lookups are case-insensitive).
            _registry.Unregister("sftp", entry.Id);
            _entries.Remove(id);
        }

        // Dispose (graceful network disconnect) outside the lock.
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

    // Must be called with _lock held.
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
