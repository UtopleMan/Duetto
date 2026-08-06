using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

// In-memory IAzureClientAdapter for provider/manager/stream unit tests — no network, no SDK.
// Mirrors blob delimiter listing: names with a '/' after the prefix collapse into folder entries.
internal sealed class FakeAzureClientAdapter : IAzureClientAdapter
{
    private readonly Dictionary<string, Dictionary<string, (byte[] Data, DateTime Mtime)>> containers = new();

    public bool Connected { get; private set; }

    // Counters let tests prove a move went server-side (CopyBlob, no client read).
    public int CopyCount { get; private set; }
    public int ReadCount { get; private set; }

    public FakeAzureClientAdapter(params string[] containerNames)
    {
        foreach (var name in containerNames)
            containers[name] = new Dictionary<string, (byte[], DateTime)>();
    }

    public void Seed(string container, string key, byte[] data) => Container(container)[key] = (data, DateTime.UtcNow);

    private Dictionary<string, (byte[] Data, DateTime Mtime)> Container(string container) =>
        containers.TryGetValue(container, out var c) ? c : throw new FileNotFoundException($"No such container: {container}");

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

    public IReadOnlyList<string> ListContainers() => containers.Keys.ToList();

    public IReadOnlyList<AzureEntry> ListBlobs(string container, string prefix)
    {
        var entries = new List<AzureEntry>();
        var folders = new HashSet<string>();

        foreach (var (key, value) in Container(container))
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
                    entries.Add(new AzureEntry(folder, $"/{container}/{prefix}{folder}", IsDirectory: true, IsReadOnly: false, Length: -1, LastWriteTimeUtc: default));
            }
            else if (!key.EndsWith('/'))
            {
                entries.Add(new AzureEntry(rest, $"/{container}/{key}", IsDirectory: false, IsReadOnly: false, value.Data.Length, value.Mtime));
            }
        }

        return entries;
    }

    public AzureEntry? StatBlob(string container, string key) =>
        Container(container).TryGetValue(key, out var v)
            ? new AzureEntry(Leaf(key), $"/{container}/{key}", IsDirectory: false, IsReadOnly: false, v.Data.Length, v.Mtime)
            : null;

    public bool PrefixExists(string container, string prefix) =>
        Container(container).Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));

    public void PutEmptyBlob(string container, string key) => Container(container)[key] = ([], DateTime.UtcNow);

    public Stream OpenRead(string container, string key)
    {
        ReadCount++;
        return Container(container).TryGetValue(key, out var v)
            ? new MemoryStream(v.Data, writable: false)
            : throw new FileNotFoundException($"No such key: {key}");
    }

    public Stream OpenWrite(string container, string key) =>
        AzureFileStream.ForWrite(body =>
        {
            using var ms = new MemoryStream();
            body.CopyTo(ms);
            Container(container)[key] = (ms.ToArray(), DateTime.UtcNow);
        });

    public void DeleteBlob(string container, string key) => Container(container).Remove(key);

    public void DeletePrefix(string container, string prefix)
    {
        var doomed = Container(container).Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in doomed)
            Container(container).Remove(key);
    }

    public bool CopyBlob(string srcContainer, string srcKey, string dstContainer, string dstKey, Action<long> onBytesCopied, CancellationToken token)
    {
        CopyCount++;
        var (data, _) = Container(srcContainer)[srcKey];
        Container(dstContainer)[dstKey] = (data, DateTime.UtcNow);
        onBytesCopied(data.Length);
        return true;
    }

    public IEnumerable<AzureEntry> EnumerateRecursive(string container, string prefix)
    {
        foreach (var (key, value) in Container(container))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.EndsWith('/'))
                continue;
            yield return new AzureEntry(Leaf(key), $"/{container}/{key}", IsDirectory: false, IsReadOnly: false, value.Data.Length, value.Mtime);
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
