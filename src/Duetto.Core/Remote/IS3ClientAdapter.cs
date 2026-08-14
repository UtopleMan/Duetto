namespace Duetto.Core.Remote;

public interface IS3ClientAdapter : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    void Disconnect();

    IReadOnlyList<string> ListBuckets();

    IReadOnlyList<S3Entry> ListObjects(string bucket, string prefix);

    S3Entry? StatObject(string bucket, string key);

    bool PrefixExists(string bucket, string prefix);

    void PutEmptyObject(string bucket, string key);

    Stream OpenRead(string bucket, string key);

    Stream OpenWrite(string bucket, string key);

    void DeleteObject(string bucket, string key);

    void DeletePrefix(string bucket, string prefix);

    bool CopyObject(string srcBucket, string srcKey, string dstBucket, string dstKey, Action<long> onBytesCopied, CancellationToken token);

    IEnumerable<S3Entry> EnumerateRecursive(string bucket, string prefix);
}

public interface IS3ClientFactory
{
    IS3ClientAdapter Create(S3ConnectionInfo info, ConnectSecret secret);
}

public sealed class DefaultS3ClientFactory : IS3ClientFactory
{
    public IS3ClientAdapter Create(S3ConnectionInfo info, ConnectSecret secret) =>
        new RealS3ClientAdapter(info, secret);
}
