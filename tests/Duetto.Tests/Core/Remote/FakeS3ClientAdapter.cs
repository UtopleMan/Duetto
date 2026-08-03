using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

// In-memory IS3ClientAdapter for provider/manager/stream unit tests — no network, no AWSSDK.
// Mirrors S3 delimiter listing: keys with a '/' after the prefix collapse into folder entries.
internal sealed class FakeS3ClientAdapter : IS3ClientAdapter
{
    private readonly Dictionary<string, Dictionary<string, (byte[] Data, DateTime Mtime)>> buckets = new();

    public bool Connected { get; private set; }

    // Counters let tests prove a move went server-side (CopyObject, no client read).
    public int CopyCount { get; private set; }
    public int ReadCount { get; private set; }

    public FakeS3ClientAdapter(params string[] bucketNames)
    {
        foreach (var name in bucketNames)
            buckets[name] = new Dictionary<string, (byte[], DateTime)>();
    }

    public void Seed(string bucket, string key, byte[] data) => Bucket(bucket)[key] = (data, DateTime.UtcNow);

    private Dictionary<string, (byte[] Data, DateTime Mtime)> Bucket(string bucket) =>
        buckets.TryGetValue(bucket, out var b) ? b : throw new FileNotFoundException($"No such bucket: {bucket}");

    public bool IsConnected => Connected;

    // Set to make the next Connect() throw (simulates auth/endpoint failure).
    public Exception? NextConnectThrow { get; set; }

    public void Connect()
    {
        if (NextConnectThrow is { } ex)
        {
            NextConnectThrow = null;
            throw ex;
        }
        Connected = true;
    }

    public void Disconnect() => Connected = false;

    public IReadOnlyList<string> ListBuckets() => buckets.Keys.ToList();

    public IReadOnlyList<S3Entry> ListObjects(string bucket, string prefix)
    {
        var entries = new List<S3Entry>();
        var folders = new HashSet<string>();

        foreach (var (key, value) in Bucket(bucket))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var rest = key[prefix.Length..];
            if (rest.Length == 0)
                continue;

            var slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                var folder = rest[..slash];
                if (folders.Add(folder))
                    entries.Add(new S3Entry(folder, $"/{bucket}/{prefix}{folder}", IsDirectory: true, IsReadOnly: false, Length: -1, LastWriteTimeUtc: default));
            }
            else if (!key.EndsWith('/'))
            {
                entries.Add(new S3Entry(rest, $"/{bucket}/{key}", IsDirectory: false, IsReadOnly: false, value.Data.Length, value.Mtime));
            }
        }

        return entries;
    }

    public S3Entry? StatObject(string bucket, string key) =>
        Bucket(bucket).TryGetValue(key, out var v)
            ? new S3Entry(Leaf(key), $"/{bucket}/{key}", IsDirectory: false, IsReadOnly: false, v.Data.Length, v.Mtime)
            : null;

    public bool PrefixExists(string bucket, string prefix) =>
        Bucket(bucket).Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));

    public void PutEmptyObject(string bucket, string key) => Bucket(bucket)[key] = ([], DateTime.UtcNow);

    public Stream OpenRead(string bucket, string key)
    {
        ReadCount++;
        return Bucket(bucket).TryGetValue(key, out var v)
            ? new MemoryStream(v.Data, writable: false)
            : throw new FileNotFoundException($"No such key: {key}");
    }

    public Stream OpenWrite(string bucket, string key) =>
        S3FileStream.ForWrite(body =>
        {
            using var ms = new MemoryStream();
            body.CopyTo(ms);
            Bucket(bucket)[key] = (ms.ToArray(), DateTime.UtcNow);
        });

    public void DeleteObject(string bucket, string key) => Bucket(bucket).Remove(key);

    public void DeletePrefix(string bucket, string prefix)
    {
        var doomed = Bucket(bucket).Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in doomed)
            Bucket(bucket).Remove(key);
    }

    public bool CopyObject(string srcBucket, string srcKey, string dstBucket, string dstKey, Action<long> onBytesCopied, CancellationToken token)
    {
        CopyCount++;
        var (data, _) = Bucket(srcBucket)[srcKey];
        Bucket(dstBucket)[dstKey] = (data, DateTime.UtcNow);
        onBytesCopied(data.Length);
        return true;
    }

    public IEnumerable<S3Entry> EnumerateRecursive(string bucket, string prefix)
    {
        foreach (var (key, value) in Bucket(bucket))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.EndsWith('/'))
                continue;
            yield return new S3Entry(Leaf(key), $"/{bucket}/{key}", IsDirectory: false, IsReadOnly: false, value.Data.Length, value.Mtime);
        }
    }

    private static string Leaf(string key)
    {
        var trimmed = key.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    public void Dispose() { }
}
