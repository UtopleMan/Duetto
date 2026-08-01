namespace Duetto.Core.Remote;

// A recoverable connection drop. WithReconnect catches this (and only this) to perform one
// reconnect + retry; auth/other failures propagate. RealSmbClientAdapter raises it when the
// underlying SMB2Client reports it is no longer connected.
public sealed class SmbConnectionException : IOException
{
    public SmbConnectionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

// Login rejected (bad credentials / guest disabled). Never treated as reconnectable.
public sealed class SmbAuthenticationException : IOException
{
    public SmbAuthenticationException(string message)
        : base(message)
    {
    }
}

// Paths are provider-local ("/share", "/share/dir/file"); the adapter owns the share -> tree
// split and the '/' -> '\' translation. Never called with "/" — the provider maps the root to
// ListShares itself.
public interface ISmbClientAdapter : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    void Disconnect();

    IReadOnlyList<string> ListShares();

    IEnumerable<SmbEntry> ListDirectory(string path);

    // Null when the path does not exist.
    SmbEntry? Get(string path);

    bool IsDirectory(string path);

    bool IsFile(string path);

    void CreateDirectory(string path);

    void CreateFile(string path);

    // Same share only. replaceExisting maps to FileRenameInformation.ReplaceIfExists.
    void RenameFile(string oldPath, string newPath, bool replaceExisting);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    bool Exists(string path);

    Stream OpenRead(string path);

    // Creates or truncates the target.
    Stream OpenWrite(string path);

    void SetLastWriteTimeUtc(string path, DateTime utc);
}

// Inject a fake in tests to avoid real socket opens.
public interface ISmbClientFactory
{
    // Creates but does NOT connect the adapter.
    ISmbClientAdapter Create(SmbConnectionInfo info, ConnectSecret secret);
}

// Reconnect contract mirrors SftpConnection: on a SmbConnectionException or a pre-call
// !IsConnected, exactly one reconnect attempt + one retry; a failure on the retry propagates.
// Thread safety: Connect/Disconnect/WithReconnect are NOT thread-safe with respect to each
// other; the provider serialises concurrent calls.
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

    public ISmbClientAdapter Adapter =>
        adapter ?? throw new InvalidOperationException("SmbConnection is not connected.");

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        // Dispose any stale adapter before creating a fresh one; null it out so a failed
        // Connect leaves the connection observably disconnected instead of pointing at a
        // disposed adapter.
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
            // Single reconnect attempt — exceptions here propagate directly.
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
