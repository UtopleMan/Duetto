using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Duet.Core.FileSystem;

namespace Duet.Core.Search;

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

    /// <summary>
    /// Streams hits below <paramref name="scopeDir"/> whose name contains
    /// <paramref name="query"/> (ordinal, case-insensitive) or, when
    /// <paramref name="includeContents"/> is set, whose text content contains it.
    /// Unreadable directories are skipped. <paramref name="stats"/> updates live.
    /// </summary>
    public static async IAsyncEnumerable<SearchHit> Search(
        string scopeDir,
        string query,
        bool includeContents,
        SearchStats stats,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<SearchHit>();
        var worker = Task.Run(() =>
        {
            try
            {
                Walk(new DirectoryInfo(scopeDir), "");
            }
            finally
            {
                channel.Writer.Complete();
            }

            void Walk(DirectoryInfo dir, string relative)
            {
                ct.ThrowIfCancellationRequested();
                IEnumerable<FileSystemInfo> children;
                try
                {
                    children = dir.EnumerateFileSystemInfos();
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }

                foreach (var info in children)
                {
                    ct.ThrowIfCancellationRequested();
                    if (info is DirectoryInfo sub)
                    {
                        if (sub.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            stats.Match();
                            channel.Writer.TryWrite(new SearchHit(DirectoryLister.ToEntry(sub), relative));
                        }

                        // Recurse into real directories only; symlinked dirs would risk cycles.
                        if (sub.LinkTarget is null)
                            Walk(sub, Path.Combine(relative, sub.Name));
                        continue;
                    }

                    stats.FileScanned();
                    var nameMatch = info.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                    var match = nameMatch;
                    if (!match && includeContents && info is FileInfo file && file.Length <= MaxContentSearchBytes)
                        match = ContentContains(file, query, ct);

                    if (match)
                    {
                        stats.Match();
                        channel.Writer.TryWrite(new SearchHit(DirectoryLister.ToEntry(info), relative));
                    }
                }
            }
        }, ct);

        await foreach (var hit in channel.Reader.ReadAllAsync(ct))
            yield return hit;
        await worker.ConfigureAwait(false);
    }

    private static bool ContentContains(FileInfo file, string query, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(file.FullName);
            // NUL byte in the first chunk = treat as binary, skip.
            var buffer = new char[8192];
            var first = reader.Read(buffer, 0, buffer.Length);
            var window = new string(buffer, 0, first);
            if (window.Contains('\0'))
                return false;
            if (window.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;

            // Overlap window so a match straddling chunk boundaries is still found.
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
