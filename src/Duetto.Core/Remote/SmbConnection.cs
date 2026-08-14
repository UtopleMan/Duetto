namespace Duetto.Core.Remote;

public sealed class SmbConnectionException : IOException
{
    public SmbConnectionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class SmbAuthenticationException : IOException
{
    public SmbAuthenticationException(string message)
        : base(message)
    {
    }
}

public interface ISmbClientAdapter : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    void Disconnect();

    IReadOnlyList<string> ListShares();

    IEnumerable<SmbEntry> ListDirectory(string path);

    SmbEntry? Get(string path);

    bool IsDirectory(string path);

    bool IsFile(string path);

    void CreateDirectory(string path);

    void CreateFile(string path);

    void RenameFile(string oldPath, string newPath, bool replaceExisting);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    bool Exists(string path);

    Stream OpenRead(string path);

    Stream OpenWrite(string path);

    void SetLastWriteTimeUtc(string path, DateTime utc);

    bool ServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token);
}

public interface ISmbClientFactory
{
    ISmbClientAdapter Create(SmbConnectionInfo info, ConnectSecret secret);
}

public sealed class SmbConnection : IDisposable
{
    private readonly SmbConnectionInfo info;
    private readonly ConnectSecret secret;
    private readonly ISmbClientFactory factory;

    private ISmbClientAdapter? adapter;
    private bool disposed;

    public SmbConnection(SmbConnectionInfo info, ConnectSecret secret, ISmbClientFactory? factory = null)
    {
        this.info = info;
        this.secret = secret;
        this.factory = factory ?? new DefaultSmbClientFactory();
    }

    public bool IsConnected => adapter?.IsConnected ?? false;

    public string Host => info.Host;

    public ISmbClientAdapter Adapter =>
        adapter ?? throw new InvalidOperationException("SmbConnection is not connected.");

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        adapter?.Dispose();
        adapter = null;

        var fresh = factory.Create(info, secret);
        try
        {
            fresh.Connect();
        }
        catch (Exception)
        {
            fresh.Dispose();
            throw;
        }

        adapter = fresh;
    }

    public void Disconnect()
    {
        if (adapter is { IsConnected: true })
            adapter.Disconnect();
    }

    public T WithReconnect<T>(Func<T> op)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!IsConnected)
            Connect();

        try
        {
            return op();
        }
        catch (SmbConnectionException)
        {
            Connect();
            return op();
        }
    }

    public void WithReconnect(Action op) =>
        WithReconnect<int>(() => { op(); return 0; });

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        adapter?.Dispose();
        adapter = null;
    }
}
