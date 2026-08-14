namespace Duetto.Core.Remote;

public interface IAzureClientAdapter : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    void Disconnect();

    IReadOnlyList<string> ListContainers();

    IReadOnlyList<AzureEntry> ListBlobs(string container, string prefix);

    AzureEntry? StatBlob(string container, string key);

    bool PrefixExists(string container, string prefix);

    void PutEmptyBlob(string container, string key);

    Stream OpenRead(string container, string key);

    Stream OpenWrite(string container, string key);

    void DeleteBlob(string container, string key);

    void DeletePrefix(string container, string prefix);

    bool CopyBlob(string srcContainer, string srcKey, string dstContainer, string dstKey, Action<long> onBytesCopied, CancellationToken token);

    IEnumerable<AzureEntry> EnumerateRecursive(string container, string prefix);
}

public interface IAzureClientFactory
{
    IAzureClientAdapter Create(AzureConnectionInfo info, ConnectSecret secret);
}

public sealed class DefaultAzureClientFactory : IAzureClientFactory
{
    public IAzureClientAdapter Create(AzureConnectionInfo info, ConnectSecret secret) =>
        new RealAzureClientAdapter(info, secret);
}
