using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Duetto.Core.Remote;

public interface ISftpClientAdapter : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    void Disconnect();

    void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler);

    IEnumerable<SftpEntry> ListDirectory(string path);

    SftpEntry? Get(string path);

    bool IsDirectory(string path);

    bool IsFile(string path);

    void CreateDirectory(string path);

    void CreateFile(string path);

    void RenameFile(string oldPath, string newPath, bool isPosix = false);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    bool Exists(string path);

    Stream OpenRead(string path);

    Stream OpenWrite(string path);

    void SetLastWriteTimeUtc(string path, DateTime utc);
}

public interface ISftpClientFactory
{
    ISftpClientAdapter Create(ConnectionInfo info, ConnectSecret secret);
}

public sealed class DefaultSftpClientFactory : ISftpClientFactory
{
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

internal sealed class RealSftpClientAdapter : ISftpClientAdapter
{
    private readonly SftpClient _client;

    internal RealSftpClientAdapter(SftpClient client) => _client = client;

    public bool IsConnected => _client.IsConnected;
    public void Connect() => _client.Connect();
    public void Disconnect() => _client.Disconnect();

    public void SetHostKeyReceived(EventHandler<HostKeyEventArgs> handler) =>
        _client.HostKeyReceived += handler;

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
        _client.Create(path).Dispose();

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

public sealed class SftpConnection : IDisposable
{
    private readonly ConnectionInfo _info;
    private readonly ConnectSecret _secret;
    private readonly ISftpClientFactory _factory;
    private readonly HostKeyStore? _hostKeyStore;

    private ISftpClientAdapter? _adapter;
    private bool _disposed;

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

    public bool IsConnected => _adapter?.IsConnected ?? false;

    public ISftpClientAdapter Adapter =>
        _adapter
        ?? throw new InvalidOperationException("SftpConnection is not connected.");

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
            adapter.Dispose();
            throw;
        }

        _adapter = adapter;
    }

    public void Disconnect()
    {
        if (_adapter is { IsConnected: true })
            _adapter.Disconnect();
    }

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
            Connect();
            return op();
        }
    }

    public void WithReconnect(Action op) =>
        WithReconnect<int>(() => { op(); return 0; });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _adapter?.Dispose();
        _adapter = null;
    }
}
