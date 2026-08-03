namespace Duetto.Core.Remote;

// Abstraction over the S3 SDK so the provider never sees AWSSDK types and tests can inject an
// in-memory fake. All keys are S3 object keys (no leading slash); buckets are named separately.
// Entries returned carry a provider-local FullName ("/bucket/key"). Implementations translate SDK
// faults into S3AuthenticationException (never retried), FileNotFoundException, IOException, or
// S3ConnectionException (recoverable).
public interface IS3ClientAdapter : IDisposable
{
    bool IsConnected { get; }

    // Builds the underlying client and validates credentials/endpoint (bucket list, or a scoped
    // probe when a single bucket is configured). Throws on auth/connection failure.
    void Connect();

    // Drops the underlying client. S3 is stateless HTTP, so this just releases the client; a later
    // call reconnects via Connect().
    void Disconnect();

    IReadOnlyList<string> ListBuckets();

    // One directory level under prefix: immediate subfolders (common prefixes) + files. prefix is
    // "" for the bucket root or ends with "/" for a folder.
    IReadOnlyList<S3Entry> ListObjects(string bucket, string prefix);

    // Null when the object does not exist. key is an exact object key.
    S3Entry? StatObject(string bucket, string key);

    // True when at least one object exists under prefix (a non-empty logical folder).
    bool PrefixExists(string bucket, string prefix);

    // Writes a zero-byte object — used for empty files and for "prefix/" folder markers.
    void PutEmptyObject(string bucket, string key);

    Stream OpenRead(string bucket, string key);

    // Returns a write stream that spools locally and uploads on close (multipart for large data).
    Stream OpenWrite(string bucket, string key);

    void DeleteObject(string bucket, string key);

    // Deletes every object under prefix (recursive folder delete).
    void DeletePrefix(string bucket, string prefix);

    // Server-side CopyObject. Returns false when the object is too large for a single-part copy so
    // the caller falls back to streaming; reports bytes copied once on success.
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
