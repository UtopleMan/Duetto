using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Duetto.Core.Remote;

// Wraps AWSSDK's AmazonS3Client behind IS3ClientAdapter. AWSSDK v4 is async-only, so each SDK call
// is awaited and blocked on — the provider is synchronous and already runs transfers off the UI
// thread. All object keys are backslash-free '/'-separated S3 keys; FullName on returned entries is
// the provider-local "/bucket/key" form.
internal sealed class RealS3ClientAdapter(S3ConnectionInfo info, ConnectSecret secret) : IS3ClientAdapter
{
    // AWS caps a single-part server-side copy at 5 GiB; larger objects need multipart copy, which
    // we do not implement — the provider streams those instead.
    private const long SingleCopyLimit = 5L * 1024 * 1024 * 1024;

    private IAmazonS3? client;

    private IAmazonS3 Client => client ?? throw new S3ConnectionException("S3 client is not connected.");

    public bool IsConnected => client is not null;

    public void Connect()
    {
        client?.Dispose();
        client = null;
        // Wrap construction too: an invalid endpoint (e.g. a bare host with no scheme) makes the
        // SDK throw AmazonClientException here, which must surface as a dialog error, not a crash.
        client = Run(() => (IAmazonS3)new AmazonS3Client(BuildCredentials(), BuildConfig()));

        // Anonymous access often has only GetObject (e.g. a public "download" policy) and cannot
        // list — there is no cheap call to validate, so skip eager validation and let per-object
        // reads authorize themselves.
        if (info.AuthMode == S3AuthMode.Anonymous)
            return;

        // Validate credentials + endpoint eagerly so failures surface in the connect dialog. Creds
        // scoped to one bucket cannot ListBuckets, so probe the bucket when one is configured.
        if (string.IsNullOrEmpty(info.Bucket))
            ListBuckets();
        else
            Run(() => ListObjectsPage(info.Bucket, prefix: "", delimiter: "/", continuationToken: null, maxKeys: 1));
    }

    public void Disconnect()
    {
        client?.Dispose();
        client = null;
    }

    private AWSCredentials BuildCredentials() => info.AuthMode switch
    {
        S3AuthMode.Anonymous => new AnonymousAWSCredentials(),
        S3AuthMode.Profile => ResolveProfile(info.Profile),
        _ => string.IsNullOrEmpty(secret.SessionToken)
            ? new BasicAWSCredentials(info.AccessKeyId, secret.Password)
            : new SessionAWSCredentials(info.AccessKeyId, secret.Password, secret.SessionToken),
    };

    private static AWSCredentials ResolveProfile(string profile)
    {
        var chain = new CredentialProfileStoreChain();
        if (chain.TryGetAWSCredentials(profile, out var creds))
            return creds;
        throw new S3AuthenticationException($"AWS profile not found: {profile}");
    }

    private AmazonS3Config BuildConfig()
    {
        var config = new AmazonS3Config { ForcePathStyle = info.PathStyle };

        if (!string.IsNullOrWhiteSpace(info.Endpoint))
            config.ServiceURL = NormalizeEndpoint(info.Endpoint);
        else if (!string.IsNullOrWhiteSpace(info.Region))
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(info.Region);

        return config;
    }

    // The AWS SDK requires a scheme on ServiceURL and throws otherwise. Users commonly enter just a
    // host (e.g. "minio.example.ts.net"); default to https so it forms a valid URL. A user who needs
    // http (or a non-default port) types the full URL, which passes through unchanged.
    internal static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.Length == 0)
            return trimmed;
        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
    }

    public IReadOnlyList<string> ListBuckets() =>
        Run(() =>
        {
            var resp = Client.ListBucketsAsync().GetAwaiter().GetResult();
            return resp.Buckets?.Select(b => b.BucketName).ToList() ?? [];
        });

    public IReadOnlyList<S3Entry> ListObjects(string bucket, string prefix)
    {
        var entries = new List<S3Entry>();
        string? token = null;

        do
        {
            var resp = Run(() => ListObjectsPage(bucket, prefix, delimiter: "/", token, maxKeys: null));

            foreach (var common in resp.CommonPrefixes ?? [])
            {
                var name = LeafOfPrefix(common);
                entries.Add(new S3Entry(name, ObjectPath(bucket, common.TrimEnd('/')), IsDirectory: true, IsReadOnly: false, Length: -1, LastWriteTimeUtc: default));
            }

            foreach (var obj in resp.S3Objects ?? [])
            {
                // Skip the folder's own marker (key == prefix) and any "…/" placeholder.
                if (obj.Key == prefix || obj.Key.EndsWith('/'))
                    continue;
                entries.Add(new S3Entry(LeafOfKey(obj.Key), ObjectPath(bucket, obj.Key), IsDirectory: false, IsReadOnly: false, obj.Size ?? 0, obj.LastModified?.ToUniversalTime() ?? default));
            }

            token = resp.IsTruncated == true ? resp.NextContinuationToken : null;
        }
        while (token is not null);

        return entries;
    }

    public S3Entry? StatObject(string bucket, string key) =>
        Run(() =>
        {
            try
            {
                var meta = Client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = key }).GetAwaiter().GetResult();
                return new S3Entry(LeafOfKey(key), ObjectPath(bucket, key), IsDirectory: false, IsReadOnly: false, meta.ContentLength, meta.LastModified?.ToUniversalTime() ?? default);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return (S3Entry?)null;
            }
        });

    public bool PrefixExists(string bucket, string prefix) =>
        Run(() =>
        {
            var resp = ListObjectsPage(bucket, prefix, delimiter: null, continuationToken: null, maxKeys: 1);
            return (resp.S3Objects?.Count ?? 0) > 0;
        });

    public void PutEmptyObject(string bucket, string key) =>
        Run(() => Client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, ContentBody = "" }).GetAwaiter().GetResult());

    public Stream OpenRead(string bucket, string key) =>
        Run(() => Client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }).GetAwaiter().GetResult().ResponseStream);

    public Stream OpenWrite(string bucket, string key) =>
        S3FileStream.ForWrite(body => Run(() =>
        {
            using var transfer = new TransferUtility(Client);
            transfer.UploadAsync(new TransferUtilityUploadRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = body,
                AutoCloseStream = false,
            }).GetAwaiter().GetResult();
        }));

    public void DeleteObject(string bucket, string key) =>
        Run(() => Client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }).GetAwaiter().GetResult());

    public void DeletePrefix(string bucket, string prefix)
    {
        var keys = new List<string>();
        string? token = null;

        do
        {
            var resp = Run(() => ListObjectsPage(bucket, prefix, delimiter: null, token, maxKeys: null));
            keys.AddRange((resp.S3Objects ?? []).Select(o => o.Key));
            token = resp.IsTruncated == true ? resp.NextContinuationToken : null;
        }
        while (token is not null);

        foreach (var batch in keys.Chunk(1000))
        {
            var request = new DeleteObjectsRequest
            {
                BucketName = bucket,
                Objects = [.. batch.Select(k => new KeyVersion { Key = k })],
            };
            Run(() => Client.DeleteObjectsAsync(request).GetAwaiter().GetResult());
        }
    }

    public bool CopyObject(string srcBucket, string srcKey, string dstBucket, string dstKey, Action<long> onBytesCopied, CancellationToken token)
    {
        var meta = Run(() => Client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = srcBucket, Key = srcKey }, token).GetAwaiter().GetResult());
        if (meta.ContentLength > SingleCopyLimit)
            return false;

        Run(() => Client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = srcBucket,
            SourceKey = srcKey,
            DestinationBucket = dstBucket,
            DestinationKey = dstKey,
        }, token).GetAwaiter().GetResult());

        onBytesCopied(meta.ContentLength);
        return true;
    }

    public IEnumerable<S3Entry> EnumerateRecursive(string bucket, string prefix)
    {
        string? token = null;

        do
        {
            var resp = Run(() => ListObjectsPage(bucket, prefix, delimiter: null, token, maxKeys: null));
            foreach (var obj in resp.S3Objects ?? [])
            {
                if (obj.Key.EndsWith('/'))
                    continue;
                yield return new S3Entry(LeafOfKey(obj.Key), ObjectPath(bucket, obj.Key), IsDirectory: false, IsReadOnly: false, obj.Size ?? 0, obj.LastModified?.ToUniversalTime() ?? default);
            }
            token = resp.IsTruncated == true ? resp.NextContinuationToken : null;
        }
        while (token is not null);
    }

    private ListObjectsV2Response ListObjectsPage(string bucket, string prefix, string? delimiter, string? continuationToken, int? maxKeys) =>
        Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = prefix,
            Delimiter = delimiter,
            ContinuationToken = continuationToken,
            MaxKeys = maxKeys,
        }).GetAwaiter().GetResult();

    private static string ObjectPath(string bucket, string key) => $"/{bucket}/{key}";

    private static string LeafOfKey(string key)
    {
        var trimmed = key.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static string LeafOfPrefix(string prefix) => LeafOfKey(prefix);

    // Translates AWSSDK faults into the exception contract the connection/provider expect.
    private static T Run<T>(Func<T> op)
    {
        try
        {
            return op();
        }
        catch (AmazonS3Exception ex)
        {
            throw Translate(ex);
        }
        // AmazonClientException is the base of AmazonServiceException, and also what the SDK throws
        // for client-side faults such as an invalid ServiceURL — catch it so nothing escapes as an
        // unhandled crash.
        catch (AmazonClientException ex)
        {
            throw new S3ConnectionException(ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new S3ConnectionException(ex.Message, ex);
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            throw new S3ConnectionException(ex.Message, ex);
        }
    }

    private static void Run(Action op) => Run(() => { op(); return true; });

    private static Exception Translate(AmazonS3Exception ex)
    {
        if (ex.StatusCode == HttpStatusCode.Forbidden || ex.ErrorCode is "InvalidAccessKeyId" or "SignatureDoesNotMatch" or "AccessDenied")
            return new S3AuthenticationException(ex.Message, ex);

        if (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode is "NoSuchKey" or "NoSuchBucket")
            return new FileNotFoundException(ex.Message, ex);

        return new IOException(ex.Message, ex);
    }

    public void Dispose() => client?.Dispose();
}
