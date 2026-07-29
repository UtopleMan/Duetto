using System.Collections.ObjectModel;
using Duetto.Core.FileSystem;

namespace Duetto.Core.Remote;

/// <summary>
/// Owns live SFTP connections by id, builds <see cref="SftpFileSystemProvider"/> instances
/// from them, and registers/unregisters them in the <see cref="FileSystemRegistry"/> under the
/// <c>sftp://&lt;id&gt;/…</c> scheme.
///
/// <para>
/// <b>API summary:</b>
/// <list type="bullet">
///   <item><description><see cref="Connect"/> — connect + register (replaces any existing connection for the same id).</description></item>
///   <item><description><see cref="Disconnect(string)"/> — unregister + disconnect + dispose; no-op for unknown ids.</description></item>
///   <item><description><see cref="IsConnected(string)"/> — true when the id is tracked and the session is live.</description></item>
///   <item><description><see cref="ConnectedIds"/> — snapshot of all currently-tracked ids.</description></item>
///   <item><description><see cref="DisposeAll"/> / <see cref="Dispose"/> — disconnect + unregister everything.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Thread safety:</b> all public methods are guarded by a single lock so that UI and
/// background operations may race connect/disconnect safely.
/// </para>
///
/// <para>
/// <b>Failure contract:</b> when <see cref="Connect"/> throws (auth failure, host-key mismatch,
/// network error), the manager stays clean — nothing is registered, nothing is tracked, and the
/// failed <see cref="SftpConnection"/> is disposed before the exception propagates.
/// <see cref="HostKeyChangedException"/> propagates unchanged for the Phase 4 dialog to handle.
/// </para>
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly FileSystemRegistry _registry;
    private readonly HostKeyStore _hostKeyStore;
    private readonly ISftpClientFactory _factory;
    private readonly object _lock = new();

    /// <summary>Tracks live (connected) session+provider pairs by connection id.</summary>
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    private sealed class Entry(SftpConnection Connection, SftpFileSystemProvider Provider) : IDisposable
    {
        public readonly SftpConnection Connection = Connection;
        public readonly SftpFileSystemProvider Provider = Provider;

        public void Dispose()
        {
            Provider.Dispose();   // disposes the connection via SftpFileSystemProvider.Dispose
        }
    }

    /// <summary>
    /// Creates a <see cref="ConnectionManager"/> that registers providers into
    /// <paramref name="registry"/> and verifies host keys via <paramref name="hostKeyStore"/>.
    /// </summary>
    /// <param name="registry">The registry to register/unregister providers in.</param>
    /// <param name="hostKeyStore">TOFU host-key store; shared across all connections.</param>
    /// <param name="factory">
    ///   SFTP client factory; pass <see langword="null"/> to use the production
    ///   <see cref="DefaultSftpClientFactory"/>.  Inject a fake in tests.
    /// </param>
    public ConnectionManager(
        FileSystemRegistry registry,
        HostKeyStore hostKeyStore,
        ISftpClientFactory? factory = null)
    {
        _registry = registry;
        _hostKeyStore = hostKeyStore;
        _factory = factory ?? new DefaultSftpClientFactory();
    }

    /// <summary>
    /// A snapshot of all currently-tracked connection ids.
    /// The returned collection reflects the state at the moment of the call.
    /// </summary>
    public IReadOnlyCollection<string> ConnectedIds
    {
        get
        {
            lock (_lock)
                return _entries.Keys.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="id"/> is tracked and the underlying
    /// session is live.
    /// </summary>
    public bool IsConnected(string id)
    {
        lock (_lock)
            return _entries.TryGetValue(id, out var e) && e.Connection.IsConnected;
    }

    /// <summary>
    /// Connects to the server described by <paramref name="info"/> using
    /// <paramref name="secret"/>, builds an <see cref="SftpFileSystemProvider"/>, and registers
    /// it in the <see cref="FileSystemRegistry"/> as <c>sftp://&lt;id&gt;</c>.
    ///
    /// <para>
    /// If the id is already connected the old connection is disconnected and disposed before
    /// the new one is established (replace-on-reconnect).
    /// </para>
    ///
    /// <para>
    /// On failure (auth, host-key change, network) the manager is left clean: no registration,
    /// no tracking entry.
    /// </para>
    /// </summary>
    /// <exception cref="HostKeyChangedException">
    ///   Propagated when the server presents a key that differs from the stored pin.
    /// </exception>
    public void Connect(ConnectionInfo info, ConnectSecret secret)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Evict any existing connection for this id (replace-on-reconnect).
            if (_entries.TryGetValue(info.Id, out var old))
            {
                _registry.Unregister("sftp", info.Id);
                _entries.Remove(info.Id);
                old.Dispose();
            }

            // Build and connect — if this throws, nothing has been registered or tracked.
            var conn = new SftpConnection(info, secret, _factory, _hostKeyStore);
            try
            {
                conn.Connect();
            }
            catch (Exception)
            {
                conn.Dispose();
                throw;
            }

            var provider = new SftpFileSystemProvider(conn);
            _entries[info.Id] = new Entry(conn, provider);
            _registry.Register("sftp", info.Id, provider);
        }
    }

    /// <summary>
    /// Unregisters the provider from the registry and disposes the connection for
    /// <paramref name="id"/>.  No-op when <paramref name="id"/> is not tracked.
    /// </summary>
    public void Disconnect(string id)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out var entry))
                return;

            _registry.Unregister("sftp", id);
            _entries.Remove(id);
            entry.Dispose();
        }
    }

    /// <summary>
    /// Disconnects and disposes all tracked connections and unregisters all providers.
    /// Equivalent to <see cref="Dispose"/>.
    /// </summary>
    public void DisposeAll()
    {
        lock (_lock)
            DisposeAllLocked();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeAllLocked();
        }
    }

    /// <summary>Must be called with <c>_lock</c> held.</summary>
    private void DisposeAllLocked()
    {
        foreach (var (id, entry) in _entries)
        {
            _registry.Unregister("sftp", id);
            entry.Dispose();
        }

        _entries.Clear();
    }
}
