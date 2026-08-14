using System.Net.Http;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Duetto.Core.Remote;

internal sealed class RealAzureClientAdapter(AzureConnectionInfo info, ConnectSecret secret) : IAzureClientAdapter
{
    private BlobServiceClient? client;

    private BlobServiceClient Client => client ?? throw new AzureConnectionException("Azure client is not connected.");

    public bool IsConnected => client is not null;

    public void Connect()
    {
        client = null;
        client = Run(BuildServiceClient);

        if (info.AuthMode == AzureAuthMode.Anonymous)
            return;

        if (string.IsNullOrEmpty(info.Container))
            ListContainers();
        else
            Run(() => Container(info.Container).Exists());
    }

    public void Disconnect() => client = null;

    private BlobServiceClient BuildServiceClient() => info.AuthMode switch
    {
        AzureAuthMode.ConnectionString => new BlobServiceClient(secret.Password),
        AzureAuthMode.SharedKey => new BlobServiceClient(ServiceUri(), new StorageSharedKeyCredential(info.AccountName, secret.Password)),
        AzureAuthMode.Sas => BuildSasClient(),
        _ => new BlobServiceClient(ServiceUri()),
    };

    private BlobServiceClient BuildSasClient()
    {
        var sas = secret.Password ?? string.Empty;
        return sas.Contains("://", StringComparison.Ordinal)
            ? new BlobServiceClient(new Uri(sas))
            : new BlobServiceClient(ServiceUri(), new AzureSasCredential(sas));
    }

    private Uri ServiceUri() =>
        string.IsNullOrWhiteSpace(info.Endpoint)
            ? new Uri($"https://{info.AccountName}.blob.core.windows.net")
            : new Uri(NormalizeEndpoint(info.Endpoint));

    internal static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.Length == 0)
            return trimmed;
        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
    }

    private BlobContainerClient Container(string container) => Client.GetBlobContainerClient(container);

    private BlobClient Blob(string container, string key) => Container(container).GetBlobClient(key);

    public IReadOnlyList<string> ListContainers() =>
        Run(() => Client.GetBlobContainers().Select(c => c.Name).ToList());

    public IReadOnlyList<AzureEntry> ListBlobs(string container, string prefix)
    {
        var items = Run(() => Container(container).GetBlobsByHierarchy(BlobTraits.None, BlobStates.None, "/", prefix).ToList());

        var entries = new List<AzureEntry>();
        foreach (var item in items)
        {
            if (item.IsPrefix)
            {
                var name = LeafOfKey(item.Prefix.TrimEnd('/'));
                entries.Add(new AzureEntry(name, ObjectPath(container, item.Prefix.TrimEnd('/')), IsDirectory: true, IsReadOnly: false, Length: -1, LastWriteTimeUtc: default));
            }
            else
            {
                var b = item.Blob;
                if (b.Name == prefix || b.Name.EndsWith('/'))
                    continue;
                entries.Add(new AzureEntry(LeafOfKey(b.Name), ObjectPath(container, b.Name), IsDirectory: false, IsReadOnly: false, b.Properties.ContentLength ?? 0, b.Properties.LastModified?.UtcDateTime ?? default));
            }
        }

        return entries;
    }

    public AzureEntry? StatBlob(string container, string key) =>
        Run(() =>
        {
            try
            {
                var props = Blob(container, key).GetProperties().Value;
                return new AzureEntry(LeafOfKey(key), ObjectPath(container, key), IsDirectory: false, IsReadOnly: false, props.ContentLength, props.LastModified.UtcDateTime);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return (AzureEntry?)null;
            }
        });

    public bool PrefixExists(string container, string prefix) =>
        Run(() => Container(container).GetBlobs(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None).Any());

    public void PutEmptyBlob(string container, string key) =>
        Run(() => Blob(container, key).Upload(new MemoryStream([]), overwrite: true));

    public Stream OpenRead(string container, string key) =>
        Run(() => Blob(container, key).OpenRead());

    public Stream OpenWrite(string container, string key) =>
        AzureFileStream.ForWrite(body => Run(() => Blob(container, key).Upload(body, overwrite: true)));

    public void DeleteBlob(string container, string key) =>
        Run(() => Blob(container, key).DeleteIfExists());

    public void DeletePrefix(string container, string prefix)
    {
        var names = Run(() => Container(container).GetBlobs(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None).Select(b => b.Name).ToList());
        foreach (var name in names)
            Run(() => Blob(container, name).DeleteIfExists());
    }

    public bool CopyBlob(string srcContainer, string srcKey, string dstContainer, string dstKey, Action<long> onBytesCopied, CancellationToken token)
    {
        var src = Blob(srcContainer, srcKey);
        var dst = Blob(dstContainer, dstKey);

        if (!src.CanGenerateSasUri)
            return false;

        var length = Run(() => src.GetProperties().Value.ContentLength);
        var sasUri = src.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));

        Run(() =>
        {
            var op = dst.StartCopyFromUri(sasUri, cancellationToken: token);
            op.WaitForCompletion(token);
        });

        onBytesCopied(length);
        return true;
    }

    public IEnumerable<AzureEntry> EnumerateRecursive(string container, string prefix)
    {
        var blobs = Run(() => Container(container).GetBlobs(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None).ToList());
        foreach (var b in blobs)
        {
            if (b.Name.EndsWith('/'))
                continue;
            yield return new AzureEntry(LeafOfKey(b.Name), ObjectPath(container, b.Name), IsDirectory: false, IsReadOnly: false, b.Properties.ContentLength ?? 0, b.Properties.LastModified?.UtcDateTime ?? default);
        }
    }

    private static string ObjectPath(string container, string key) => $"/{container}/{key}";

    private static string LeafOfKey(string key)
    {
        var trimmed = key.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static T Run<T>(Func<T> op)
    {
        try
        {
            return op();
        }
        catch (RequestFailedException ex)
        {
            throw Translate(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AzureConnectionException(ex.Message, ex);
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException or FormatException)
        {
            throw new AzureConnectionException(ex.Message, ex);
        }
    }

    private static void Run(Action op) => Run(() => { op(); return true; });

    private static Exception Translate(RequestFailedException ex)
    {
        if (ex.Status == 0)
            return new AzureConnectionException(ex.Message, ex);

        if (ex.Status is 401 or 403 || ex.ErrorCode is "AuthenticationFailed" or "AuthorizationFailure" or "InvalidAuthenticationInfo")
            return new AzureAuthenticationException(ex.Message, ex);

        if (ex.Status == 404 || ex.ErrorCode is "BlobNotFound" or "ContainerNotFound")
            return new FileNotFoundException(ex.Message, ex);

        return new IOException(ex.Message, ex);
    }

    public void Dispose() => client = null;
}
