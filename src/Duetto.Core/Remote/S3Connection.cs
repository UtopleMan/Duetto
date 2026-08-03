namespace Duetto.Core.Remote;

// Holds the S3 adapter for one saved connection. S3 is stateless HTTP, so "reconnect" just rebuilds
// the AmazonS3Client. Reconnect contract mirrors SmbConnection: on an S3ConnectionException or a
// pre-call !IsConnected, exactly one reconnect + one retry; auth/other failures propagate. Not
// thread-safe with respect to itself — the provider serialises concurrent calls.
public sealed class S3Connection : IDisposable
{
    private readonly S3ConnectionInfo info;
    private readonly ConnectSecret secret;
    private readonly IS3ClientFactory factory;

    private IS3ClientAdapter? adapter;
    private bool disposed;

    public S3Connection(S3ConnectionInfo info, ConnectSecret secret, IS3ClientFactory? factory = null)
    {
        this.info = info;
        this.secret = secret;
        this.factory = factory ?? new DefaultS3ClientFactory();
    }

    public bool IsConnected => adapter?.IsConnected ?? false;

    // Server-side copy domain: two paths of the same connection can be copied server-side.
    public string ConnId => info.Id;

    // Empty means the root lists all buckets; a value scopes the root to that single bucket.
    public string ConfiguredBucket => info.Bucket;

    public IS3ClientAdapter Adapter =>
        adapter ?? throw new InvalidOperationException("S3Connection is not connected.");

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
        catch (S3ConnectionException)
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
