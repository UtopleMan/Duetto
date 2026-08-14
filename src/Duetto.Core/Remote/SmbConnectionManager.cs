using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

public sealed class SmbConnectionManager : IDisposable
{
    private readonly FileSystemRegistry registry;
    private readonly ISmbClientFactory? factory;
    private readonly Lock gate = new();

    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    private bool disposed;

    private sealed class Entry(string id, SmbConnection connection, SmbFileSystemProvider provider) : IDisposable
    {
        public string Id { get; } = id;
        public SmbConnection Connection { get; } = connection;
        public SmbFileSystemProvider Provider { get; } = provider;

        public void Dispose() => Provider.Dispose();
    }

    public SmbConnectionManager(FileSystemRegistry registry, ISmbClientFactory? factory = null)
    {
        this.registry = registry;
        this.factory = factory;
    }

    public IReadOnlyCollection<string> ConnectedIds
    {
        get
        {
            lock (gate)
                return entries.Keys.ToList().AsReadOnly();
        }
    }

    public bool IsConnected(string id)
    {
        lock (gate)
            return entries.TryGetValue(id, out var e) && e.Connection.IsConnected;
    }

    public void Connect(SmbConnectionInfo info, ConnectSecret secret)
    {
        SmbConnection conn;
        Entry? evicted;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (entries.TryGetValue(info.Id, out var old))
            {
                registry.Unregister("smb", old.Id);
                entries.Remove(info.Id);
                evicted = old;
            }
            else
            {
                evicted = null;
            }

            conn = new SmbConnection(info, secret, factory);
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

        Entry? raced;
        lock (gate)
        {
            if (disposed)
            {
                conn.Dispose();
                throw new ObjectDisposedException(nameof(SmbConnectionManager));
            }

            if (entries.TryGetValue(info.Id, out var existing))
            {
                registry.Unregister("smb", existing.Id);
                entries.Remove(info.Id);
                raced = existing;
            }
            else
            {
                raced = null;
            }

            var provider = new SmbFileSystemProvider(conn);
            entries[info.Id] = new Entry(info.Id, conn, provider);
            registry.Register("smb", info.Id, provider);
        }

        raced?.Dispose();
    }

    public void Disconnect(string id)
    {
        Entry? entry;
        lock (gate)
        {
            if (!entries.TryGetValue(id, out entry))
                return;

            registry.Unregister("smb", entry.Id);
            entries.Remove(id);
        }

        entry.Dispose();
    }

    public void DisposeAll()
    {
        List<Entry> toDispose;
        lock (gate)
            toDispose = CollectAndClearLocked();

        foreach (var e in toDispose)
            e.Dispose();
    }

    public void Dispose()
    {
        List<Entry> toDispose;
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            toDispose = CollectAndClearLocked();
        }

        foreach (var e in toDispose)
            e.Dispose();
    }

    private List<Entry> CollectAndClearLocked()
    {
        var collected = new List<Entry>(entries.Count);
        foreach (var entry in entries.Values)
        {
            registry.Unregister("smb", entry.Id);
            collected.Add(entry);
        }

        entries.Clear();
        return collected;
    }
}
