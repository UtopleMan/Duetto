using Renci.SshNet;
using Renci.SshNet.Common;

namespace Duetto.Core.Remote;

/// <summary>
/// Minimal wrapper around an <see cref="ISftpClient"/> that <see cref="SftpConnection"/> needs
/// for connect/disconnect/state queries.  SSH.NET's own <see cref="ISftpClient"/> (which extends
/// <see cref="IBaseClient"/> and therefore carries <c>Connect</c>, <c>Disconnect</c>, and
/// <c>IsConnected</c>) satisfies this interface directly, so the default factory just returns
/// the real <see cref="SftpClient"/>.  Tests supply a fake that never opens a socket.
/// </summary>
public interface ISftpClientAdapter : IDisposable
{
    /// <summary>Whether the underlying transport is currently connected and authenticated.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the SSH transport and authenticates.</summary>
    void Connect();

    /// <summary>Closes the SSH transport gracefully.</summary>
    void Disconnect();

    /// <summary>Wires the host-key verification callback before the first handshake.</summary>
    void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler);

    /// <summary>Returns the underlying <see cref="ISftpClient"/> for provider operations.</summary>
    ISftpClient Client { get; }
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
/// </summary>
internal sealed class RealSftpClientAdapter : ISftpClientAdapter
{
    private readonly SftpClient _client;

    internal RealSftpClientAdapter(SftpClient client) => _client = client;

    public bool IsConnected => _client.IsConnected;
    public void Connect() => _client.Connect();
    public void Disconnect() => _client.Disconnect();
    public ISftpClient Client => _client;

    public void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler) =>
        _client.HostKeyReceived += handler;

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
    /// Returns the underlying <see cref="ISftpClient"/> for provider operations.
    /// Throws <see cref="InvalidOperationException"/> when not connected.
    /// </summary>
    public ISftpClient Client =>
        _adapter?.Client
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

        // Dispose any stale adapter before creating a fresh one.
        _adapter?.Dispose();

        var adapter = _factory.Create(_info, _secret);

        if (_hostKeyStore is not null)
            adapter.SetHostKeyReceived(_hostKeyStore.HandleHostKeyReceived);

        adapter.Connect();
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
