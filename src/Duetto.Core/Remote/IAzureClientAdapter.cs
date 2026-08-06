namespace Duetto.Core.Remote;

// Abstraction over the Azure.Storage.Blobs SDK so the provider never sees SDK types and tests can
// inject an in-memory fake. All keys are blob names (no leading slash); containers are named
// separately. Entries returned carry a provider-local FullName ("/container/blob"). Implementations
// translate SDK faults into AzureAuthenticationException (never retried), FileNotFoundException,
// IOException, or AzureConnectionException (recoverable).
public interface IAzureClientAdapter : IDisposable
{
    bool IsConnected { get; }

    // Builds the underlying client and validates credentials/endpoint (container list, or a scoped
    // probe when a single container is configured). Throws on auth/connection failure.
    void Connect();

    // Drops the underlying client. Blob storage is stateless HTTP, so this just releases the client;
    // a later call reconnects via Connect().
    void Disconnect();

    IReadOnlyList<string> ListContainers();

    // One directory level under prefix: immediate subfolders (common prefixes) + blobs. prefix is
    // "" for the container root or ends with "/" for a folder.
    IReadOnlyList<AzureEntry> ListBlobs(string container, string prefix);

    // Null when the blob does not exist. key is an exact blob name.
    AzureEntry? StatBlob(string container, string key);

    // True when at least one blob exists under prefix (a non-empty logical folder).
    bool PrefixExists(string container, string prefix);

    // Writes a zero-byte blob — used for empty files and for "prefix/" folder markers.
    void PutEmptyBlob(string container, string key);

    Stream OpenRead(string container, string key);

    // Returns a write stream that spools locally and uploads on close.
    Stream OpenWrite(string container, string key);

    void DeleteBlob(string container, string key);

    // Deletes every blob under prefix (recursive folder delete).
    void DeletePrefix(string container, string prefix);

    // Server-side Copy Blob (same account). Returns false when a server-side copy is unavailable
    // (e.g. the credential cannot mint a readable source SAS) so the caller falls back to streaming;
    // reports bytes copied once on success.
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
