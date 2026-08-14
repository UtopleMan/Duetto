using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Duetto.Core.FileSystem;

namespace Duetto.Core.Search;

public sealed record SearchHit(FileEntry Entry, string RelativeFolder);

public sealed class SearchStats
{
    private int _filesScanned;
    private int _matches;

    public int FilesScanned => _filesScanned;
    public int Matches => _matches;

    internal void FileScanned() => Interlocked.Increment(ref _filesScanned);
    internal void Match() => Interlocked.Increment(ref _matches);
}

public static class SearchService
{
    private const long MaxContentSearchBytes = 4 * 1024 * 1024;

    private static readonly FileSystemRegistry _defaultRegistry = new();

    public static IAsyncEnumerable<SearchHit> Search(
        string scopeDir,
        string query,
        bool includeContents,
        SearchStats stats,
        CancellationToken ct = default)
        => Search(scopeDir, query, includeContents, stats, _defaultRegistry, ct);

    public static async IAsyncEnumerable<SearchHit> Search(
        string scopeDir,
        string query,
        bool includeContents,
        SearchStats stats,
        FileSystemRegistry registry,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (provider, localPath) = registry.Resolve(scopeDir);

        if (!provider.Capabilities.SupportsSearch)
            yield break;

        var sep = provider.Capabilities.Separator;
        var scopeBase = localPath.TrimEnd(sep);
        var channel = Channel.CreateUnbounded<SearchHit>();
        var worker = Task.Run(() =>
        {
            try
            {
                foreach (var entry in provider.EnumerateRecursive(localPath))
                {
                    ct.ThrowIfCancellationRequested();

                    var entryParent = entry.FullPath.Contains(sep)
                        ? entry.FullPath[..entry.FullPath.LastIndexOf(sep)]
                        : "";
                    var relativeFolder = entryParent.Length > scopeBase.Length
                        ? entryParent[(scopeBase.Length + 1)..]
                        : "";

                    if (entry.IsDirectory)
                    {
                        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            stats.Match();
                            channel.Writer.TryWrite(new SearchHit(entry, relativeFolder));
                        }

                        continue;
                    }

                    stats.FileScanned();
                    var nameMatch = entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                    var match = nameMatch;
                    if (!match && includeContents && entry.SizeBytes <= MaxContentSearchBytes)
                        match = ContentContains(provider, entry.FullPath, query, ct);

                    if (match)
                    {
                        stats.Match();
                        channel.Writer.TryWrite(new SearchHit(entry, relativeFolder));
                    }
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        await foreach (var hit in channel.Reader.ReadAllAsync(ct))
            yield return hit;
        await worker.ConfigureAwait(false);
    }

    private static bool ContentContains(
        IFileSystemProvider provider,
        string path,
        string query,
        CancellationToken ct)
    {
        try
        {
            using var stream = provider.OpenRead(path);
            using var reader = new StreamReader(stream);
            var buffer = new char[8192];
            var first = reader.Read(buffer, 0, buffer.Length);
            var window = new string(buffer, 0, first);
            if (window.Contains('\0'))
                return false;
            if (window.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;

            var carry = window.Length >= query.Length ? window[^(query.Length - 1)..] : window;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = carry + new string(buffer, 0, read);
                if (chunk.Contains(query, StringComparison.OrdinalIgnoreCase))
                    return true;
                carry = chunk.Length >= query.Length ? chunk[^(query.Length - 1)..] : chunk;
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
