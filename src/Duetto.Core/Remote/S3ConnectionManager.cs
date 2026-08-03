using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

// S3 analogue of SmbConnectionManager: owns live S3 connections, registers each provider under
// scheme "s3", and mirrors the same lock / evict-outside-lock discipline so state queries stay
// responsive during a slow connect (the credential/endpoint validation in S3Connection.Connect can
// take seconds or hang).
public sealed class S3ConnectionManager : IDisposable
{
    private readonly FileSystemRegistry registry;
    private readonly IS3ClientFactory? factory;
    private readonly Lock gate = new();

    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    private bool disposed;

    private sealed class Entry(string id, S3Connection connection, S3FileSystemProvider provider) : IDisposable
    {
        public string Id { get; } = id;
        public S3Connection Connection { get; } = connection;
        public S3FileSystemProvider Provider { get; } = provider;

        public void Dispose() => Provider.Dispose();
    }

    public S3ConnectionManager(FileSystemRegistry registry, IS3ClientFactory? factory = null)
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

    // The validation handshake can take seconds or hang, so it runs OUTSIDE the manager lock —
    // concurrent IsConnected / ConnectedIds / Disconnect / Dispose calls stay responsive. Races
    // during the unlocked window resolve last-writer-wins.
    public void Connect(S3ConnectionInfo info, ConnectSecret secret)
    {
        S3Connection conn;
        Entry? evicted;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (entries.TryGetValue(info.Id, out var old))
            {
                registry.Unregister("s3", old.Id);
                entries.Remove(info.Id);
                evicted = old;
            }
            else
            {
                evicted = null;
            }

            conn = new S3Connection(info, secret, factory);
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
                throw new ObjectDisposedException(nameof(S3ConnectionManager));
            }

            if (entries.TryGetValue(info.Id, out var existing))
            {
                registry.Unregister("s3", existing.Id);
                entries.Remove(info.Id);
                raced = existing;
            }
            else
            {
                raced = null;
            }

            var provider = new S3FileSystemProvider(conn);
            entries[info.Id] = new Entry(info.Id, conn, provider);
            registry.Register("s3", info.Id, provider);
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

            registry.Unregister("s3", entry.Id);
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
            registry.Unregister("s3", entry.Id);
            collected.Add(entry);
        }

        entries.Clear();
        return collected;
    }
}
