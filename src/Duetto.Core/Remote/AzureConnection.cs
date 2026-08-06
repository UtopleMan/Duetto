namespace Duetto.Core.Remote;

// Holds the Azure adapter for one saved connection. Blob storage is stateless HTTP, so "reconnect"
// just rebuilds the BlobServiceClient. Reconnect contract mirrors S3Connection: on an
// AzureConnectionException or a pre-call !IsConnected, exactly one reconnect + one retry;
// auth/other failures propagate. Not thread-safe with respect to itself — the provider serialises
// concurrent calls.
public sealed class AzureConnection : IDisposable
{
    private readonly AzureConnectionInfo info;
    private readonly ConnectSecret secret;
    private readonly IAzureClientFactory factory;

    private IAzureClientAdapter? adapter;
    private bool disposed;

    public AzureConnection(AzureConnectionInfo info, ConnectSecret secret, IAzureClientFactory? factory = null)
    {
        this.info = info;
        this.secret = secret;
        this.factory = factory ?? new DefaultAzureClientFactory();
    }

    public bool IsConnected => adapter?.IsConnected ?? false;

    // Server-side copy domain: two paths of the same connection can be copied server-side.
    public string ConnId => info.Id;

    // Empty means the root lists all containers; a value scopes the root to that single container.
    public string ConfiguredContainer => info.Container;

    public IAzureClientAdapter Adapter =>
        adapter ?? throw new InvalidOperationException("AzureConnection is not connected.");

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
        catch (AzureConnectionException)
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
