namespace Duetto.Core.Remote;

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

    public string ConnId => info.Id;

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
