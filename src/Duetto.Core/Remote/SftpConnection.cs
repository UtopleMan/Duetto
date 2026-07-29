using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Duetto.Core.Remote;

/// <summary>
/// Minimal wrapper around an <see cref="ISftpClient"/> that <see cref="SftpConnection"/> needs
/// for connect/disconnect/state queries, plus the narrow set of SFTP operations that
/// <c>SftpFileSystemProvider</c> requires.
///
/// <para>
/// Design rationale: <see cref="ISftpClient"/> has ~90 members and <c>ISftpFile</c> has ~30;
/// exposing only these ops — and returning the thin <see cref="SftpEntry"/> record instead of
/// SSH.NET types — keeps the test fake small and avoids implementing large SSH.NET interfaces in tests.
/// The real adapter maps each call to the underlying <see cref="ISftpClient"/>;
/// the test fake backs them with an in-memory tree.
/// The mapping is one-liner mechanical delegation — see <see cref="RealSftpClientAdapter"/>.
/// </para>
/// </summary>
public interface ISftpClientAdapter : IDisposable
{
    // ── transport ────────────────────────────────────────────────────────────

    /// <summary>Whether the underlying transport is currently connected and authenticated.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the SSH transport and authenticates.</summary>
    void Connect();

    /// <summary>Closes the SSH transport gracefully.</summary>
    void Disconnect();

    /// <summary>Wires the host-key verification callback before the first handshake.</summary>
    void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler);

    // ── narrow SFTP ops (the only surface SftpFileSystemProvider needs) ──────

    /// <summary>
    /// Lists the entries under <paramref name="path"/> as thin <see cref="SftpEntry"/> values,
    /// including "." and ".." entries (the provider filters them out).
    /// </summary>
    IEnumerable<SftpEntry> ListDirectory(string path);

    /// <summary>
    /// Returns a thin <see cref="SftpEntry"/> for <paramref name="path"/>, or
    /// <see langword="null"/> when the path does not exist.
    /// </summary>
    SftpEntry? Get(string path);

    /// <summary>Returns <see langword="true"/> when <paramref name="path"/> is a directory.</summary>
    bool IsDirectory(string path);

    /// <summary>Returns <see langword="true"/> when <paramref name="path"/> is a regular file.</summary>
    bool IsFile(string path);

    /// <summary>Creates a directory at <paramref name="path"/>.</summary>
    void CreateDirectory(string path);

    /// <summary>Creates (or truncates) an empty file at <paramref name="path"/> and immediately closes it.</summary>
    void CreateFile(string path);

    /// <summary>
    /// Renames <paramref name="oldPath"/> to <paramref name="newPath"/>.
    /// When <paramref name="isPosix"/> is <see langword="true"/> the rename is atomic and
    /// replaces an existing target (POSIX-rename extension).
    /// </summary>
    void RenameFile(string oldPath, string newPath, bool isPosix = false);

    /// <summary>Deletes a regular file at <paramref name="path"/>.</summary>
    void DeleteFile(string path);

    /// <summary>Deletes an empty directory at <paramref name="path"/>.</summary>
    void DeleteDirectory(string path);

    /// <summary>Returns <see langword="true"/> when <paramref name="path"/> exists (any type).</summary>
    bool Exists(string path);

    /// <summary>Opens <paramref name="path"/> for sequential reading.</summary>
    Stream OpenRead(string path);

    /// <summary>Opens <paramref name="path"/> for writing, creating or truncating it.</summary>
    Stream OpenWrite(string path);

    /// <summary>Sets the last-write time on <paramref name="path"/> to <paramref name="utc"/>.</summary>
    void SetLastWriteTimeUtc(string path, DateTime utc);
}

/// <summary>
/// Factory that produces an <see cref="ISftpClientAdapter"/> from a <see cref="ConnectionInfo"/>
/// and a <see cref="ConnectSecret"/>.  Inject a fake in tests to avoid real socket opens.
/// </summary>
public interface ISftpClientFactory
{
    /// <summary>
    /// Creates (but does NOT connect) an adapter wrapping an SFTP client configured from
    /// <paramref name="info"/> and authenticated via <paramref name="secret"/>.
    /// </summary>
    ISftpClientAdapter Create(ConnectionInfo info, ConnectSecret secret);
}

/// <summary>
/// Default production factory.  Builds a real <see cref="SftpClient"/> using SSH.NET's
/// <see cref="Renci.SshNet.ConnectionInfo"/> and the supplied secret.
/// </summary>
public sealed class DefaultSftpClientFactory : ISftpClientFactory
{
    /// <inheritdoc/>
    public ISftpClientAdapter Create(ConnectionInfo info, ConnectSecret secret)
    {
        Renci.SshNet.AuthenticationMethod authMethod = info.AuthMode switch
        {
            AuthMode.Key => BuildKeyAuth(info, secret),
            _ => new PasswordAuthenticationMethod(info.Username, secret.Password ?? string.Empty),
        };

        var sshConnInfo = new Renci.SshNet.ConnectionInfo(
            info.Host,
            info.Port,
            info.Username,
            authMethod);

        var client = new SftpClient(sshConnInfo);
        return new RealSftpClientAdapter(client);
    }

    private static PrivateKeyAuthenticationMethod BuildKeyAuth(ConnectionInfo info, ConnectSecret secret)
    {
        if (string.IsNullOrWhiteSpace(info.KeyPath))
            throw new InvalidOperationException(
                $"ConnectionInfo '{info.Id}' uses AuthMode.Key but KeyPath is not set.");

        PrivateKeyFile keyFile = secret.KeyPassphrase is { Length: > 0 } pp
            ? new PrivateKeyFile(info.KeyPath, pp)
            : new PrivateKeyFile(info.KeyPath);

        return new PrivateKeyAuthenticationMethod(info.Username, keyFile);
    }
}

/// <summary>
/// Production adapter that wraps a real <see cref="SftpClient"/>.
/// Each narrow op is a one-liner mechanical delegation to the underlying client.
/// </summary>
internal sealed class RealSftpClientAdapter : ISftpClientAdapter
{
    private readonly SftpClient _client;

    internal RealSftpClientAdapter(SftpClient client) => _client = client;

    // ── transport ────────────────────────────────────────────────────────────

    public bool IsConnected => _client.IsConnected;
    public void Connect() => _client.Connect();
    public void Disconnect() => _client.Disconnect();

    public void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler) =>
        _client.HostKeyReceived += handler;

    // ── narrow SFTP ops ──────────────────────────────────────────────────────

    public IEnumerable<SftpEntry> ListDirectory(string path) =>
        _client.ListDirectory(path).Select(ToEntry);

    public SftpEntry? Get(string path)
    {
        try { return ToEntry(_client.Get(path)); }
        catch (SftpPathNotFoundException) { return null; }
    }

    public bool IsDirectory(string path)
    {
        try { return _client.GetAttributes(path).IsDirectory; }
        catch (SftpPathNotFoundException) { return false; }
    }

    public bool IsFile(string path)
    {
        try { return _client.GetAttributes(path).IsRegularFile; }
        catch (SftpPathNotFoundException) { return false; }
    }

    public void CreateDirectory(string path) =>
        _client.CreateDirectory(path);

    public void CreateFile(string path) =>
        _client.Create(path).Dispose();   // Create() returns an open SftpFileStream; close it immediately

    public void RenameFile(string oldPath, string newPath, bool isPosix = false) =>
        _client.RenameFile(oldPath, newPath, isPosix);

    public void DeleteFile(string path) =>
        _client.DeleteFile(path);

    public void DeleteDirectory(string path) =>
        _client.DeleteDirectory(path);

    public bool Exists(string path) =>
        _client.Exists(path);

    public Stream OpenRead(string path) =>
        _client.OpenRead(path);

    public Stream OpenWrite(string path) =>
        _client.OpenWrite(path);

    public void SetLastWriteTimeUtc(string path, DateTime utc) =>
        _client.SetLastWriteTimeUtc(path, utc);

    /// <summary>Maps an <see cref="ISftpFile"/> to the thin <see cref="SftpEntry"/> value.</summary>
    private static SftpEntry ToEntry(ISftpFile f) => new(
        Name: f.Name,
        FullName: f.FullName,
        IsDirectory: f.IsDirectory,
        IsSymbolicLink: f.IsSymbolicLink,
        Length: f.Length,
        LastWriteTimeUtc: f.LastWriteTimeUtc,
        OwnerCanRead: f.OwnerCanRead,
        OwnerCanWrite: f.OwnerCanWrite,
        OwnerCanExecute: f.OwnerCanExecute,
        GroupCanRead: f.GroupCanRead,
        GroupCanWrite: f.GroupCanWrite,
        GroupCanExecute: f.GroupCanExecute,
        OthersCanRead: f.OthersCanRead,
        OthersCanWrite: f.OthersCanWrite,
        OthersCanExecute: f.OthersCanExecute);

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Manages the lifecycle of a single SFTP session: connect, disconnect, reconnect on drop.
///
/// <para>
/// <b>Reconnect contract (for Task F / <c>SftpFileSystemProvider</c>):</b><br/>
/// Call <see cref="WithReconnect{T}(Func{T})"/> to wrap any provider operation.  On first
/// execution the client must already be connected (call <see cref="Connect"/> once at
/// provider-open time).  If the operation throws <see cref="SshConnectionException"/> or the
/// client reports <c>!IsConnected</c> before the call, the helper performs exactly one
/// reconnect attempt and retries the operation once.  A failure on the retry propagates
/// unchanged to the caller — no further retry is attempted.
/// </para>
///
/// <para>
/// The void overload <see cref="WithReconnect(Action)"/> delegates to the typed overload and
/// should be used for operations that return nothing.
/// </para>
///
/// <para>
/// <b>Thread safety:</b> Connect/Disconnect/WithReconnect are NOT thread-safe with respect to
/// each other; the provider must serialise concurrent calls if needed.
/// </para>
/// </summary>
public sealed class SftpConnection : IDisposable
{
    private readonly ConnectionInfo _info;
    private readonly ConnectSecret _secret;
    private readonly ISftpClientFactory _factory;
    private readonly HostKeyStore? _hostKeyStore;

    private ISftpClientAdapter? _adapter;
    private bool _disposed;

    /// <summary>
    /// Creates an <see cref="SftpConnection"/> that is ready to connect but not yet connected.
    /// </summary>
    /// <param name="info">Immutable descriptor for the remote host.</param>
    /// <param name="secret">Ephemeral credentials for this session.</param>
    /// <param name="factory">
    ///   Client factory; pass <see langword="null"/> to use the default production factory
    ///   (<see cref="DefaultSftpClientFactory"/>).
    /// </param>
    /// <param name="hostKeyStore">
    ///   Optional TOFU store.  When supplied, its <see cref="HostKeyStore.HandleHostKeyReceived"/>
    ///   is wired to the underlying client before each <c>Connect</c> call.
    /// </param>
    public SftpConnection(
        ConnectionInfo info,
        ConnectSecret secret,
        ISftpClientFactory? factory = null,
        HostKeyStore? hostKeyStore = null)
    {
        _info = info;
        _secret = secret;
        _factory = factory ?? new DefaultSftpClientFactory();
        _hostKeyStore = hostKeyStore;
    }

    /// <summary>Returns <see langword="true"/> when the underlying client is connected.</summary>
    public bool IsConnected => _adapter?.IsConnected ?? false;

    /// <summary>
    /// Returns the underlying <see cref="ISftpClientAdapter"/> for provider operations.
    /// Throws <see cref="InvalidOperationException"/> when not connected.
    /// </summary>
    public ISftpClientAdapter Adapter =>
        _adapter
        ?? throw new InvalidOperationException("SftpConnection is not connected.");

    /// <summary>
    /// Opens the SSH session.  Creates a new client adapter via the factory, wires host-key
    /// verification, then calls Connect.
    /// </summary>
    /// <exception cref="HostKeyChangedException">
    ///   Re-thrown from <see cref="HostKeyStore.HandleHostKeyReceived"/> when the server's key
    ///   has changed since the last trusted connection.
    /// </exception>
    public void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Dispose any stale adapter before creating a fresh one; null it out so a failed
        // Connect below leaves the connection observably disconnected instead of pointing
        // at a disposed adapter.
        _adapter?.Dispose();
        _adapter = null;

        var adapter = _factory.Create(_info, _secret);
        try
        {
            if (_hostKeyStore is not null)
                adapter.SetHostKeyReceived(_hostKeyStore.HandleHostKeyReceived);

            adapter.Connect();
        }
        catch (Exception)
        {
            // Failed handshake / bad credentials / HostKeyChangedException: the fresh
            // adapter was never published to _adapter, so dispose it here and rethrow.
            adapter.Dispose();
            throw;
        }

        _adapter = adapter;
    }

    /// <summary>Closes the SSH session gracefully.  Safe to call when already disconnected.</summary>
    public void Disconnect()
    {
        if (_adapter is { IsConnected: true })
            _adapter.Disconnect();
    }

    /// <summary>
    /// Executes <paramref name="op"/> with a single automatic reconnect on connection drop.
    ///
    /// <para>
    /// Reconnect is triggered when:
    /// <list type="bullet">
    ///   <item><description>The client reports <c>!IsConnected</c> before the call; or</description></item>
    ///   <item><description>The operation throws <see cref="SshConnectionException"/>.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// After one reconnect attempt the operation is retried once.  Any exception on the retry
    /// propagates to the caller without further recovery.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Return type of the operation.</typeparam>
    /// <param name="op">The SFTP operation to execute.</param>
    /// <returns>The value returned by <paramref name="op"/>.</returns>
    /// <exception cref="SshConnectionException">
    ///   Propagated when the retry also fails with a connection error.
    /// </exception>
    public T WithReconnect<T>(Func<T> op)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsConnected)
            Connect();

        try
        {
            return op();
        }
        catch (SshConnectionException)
        {
            // Single reconnect attempt — exceptions here propagate directly.
            Connect();
            return op();
        }
    }

    /// <summary>
    /// Void overload of <see cref="WithReconnect{T}(Func{T})"/>.
    /// See that method for the full reconnect contract.
    /// </summary>
    public void WithReconnect(Action op) =>
        WithReconnect<int>(() => { op(); return 0; });

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _adapter?.Dispose();
        _adapter = null;
    }
}
