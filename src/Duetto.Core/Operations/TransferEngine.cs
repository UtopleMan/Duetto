using System.Collections.Concurrent;
using System.Diagnostics;
using Duetto.Core.FileSystem;

namespace Duetto.Core.Operations;

public enum TransferMode
{
    Copy,
    Move,
}

public enum TransferFileStatus
{
    Queued,
    InProgress,
    Done,
    Skipped,
}

public sealed record SkippedFile(string SourcePath, string Reason);

public sealed record TransferFileState(
    string SourcePath,
    string DestinationPath,
    long SizeBytes,
    TransferFileStatus Status,
    double Percent);

/// <summary>Immutable snapshot of a running transfer, safe to hand to the UI thread.</summary>
public sealed record TransferSnapshot(
    TransferMode Mode,
    string DestinationDir,
    int TotalFiles,
    int FilesDone,
    int FilesSkipped,
    long TotalBytes,
    long BytesDone,
    string? CurrentFileName,
    long CurrentFileBytesDone,
    long CurrentFileSize,
    double BytesPerSecond,
    TimeSpan? Remaining,
    bool IsPaused,
    bool IsComplete,
    bool IsCancelled,
    IReadOnlyList<SkippedFile> Skipped);

public sealed class TransferSession : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _resume = new(true);
    private readonly ConcurrentDictionary<string, TransferFileState> _states = new();
    private readonly List<SkippedFile> _skipped = [];
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();

    private long _bytesDone;
    private int _filesDone;
    private string? _currentFileName;
    private long _currentFileBytesDone;
    private long _currentFileSize;
    private volatile bool _complete;

    public TransferMode Mode { get; }
    public string DestinationDir { get; }
    public int TotalFiles { get; private set; }
    public long TotalBytes { get; private set; }
    public Task Completion { get; internal set; } = Task.CompletedTask;
    public bool IsPaused => !_resume.IsSet;
    public bool IsCancelled => _cts.IsCancellationRequested;

    /// <summary>Raised from the worker thread whenever progress advances.</summary>
    public event Action? Changed;

    internal TransferSession(TransferMode mode, string destinationDir)
    {
        Mode = mode;
        DestinationDir = destinationDir;
    }

    public void Pause()
    {
        _resume.Reset();
        Changed?.Invoke();
    }

    public void Resume()
    {
        _resume.Set();
        Changed?.Invoke();
    }

    public void Cancel()
    {
        _cts.Cancel();
        _resume.Set();
        Changed?.Invoke();
    }

    public TransferFileStatus? StatusOf(string sourcePath) =>
        _states.TryGetValue(sourcePath, out var s) ? s.Status : null;

    public TransferFileState? StateOf(string sourcePath) =>
        _states.TryGetValue(sourcePath, out var s) ? s : null;

    public TransferSnapshot Snapshot()
    {
        lock (_gate)
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            var speed = elapsed > 0.2 ? _bytesDone / elapsed : 0;
            TimeSpan? remaining = speed > 1 && TotalBytes > _bytesDone
                ? TimeSpan.FromSeconds((TotalBytes - _bytesDone) / speed)
                : null;
            return new TransferSnapshot(
                Mode, DestinationDir, TotalFiles, _filesDone, _skipped.Count,
                TotalBytes, _bytesDone,
                _currentFileName, _currentFileBytesDone, _currentFileSize,
                speed, remaining, IsPaused, _complete, IsCancelled, _skipped.ToArray());
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
        _resume.Dispose();
    }

    internal CancellationToken Token => _cts.Token;
    internal void WaitIfPaused() => _resume.Wait(Token);

    internal void Plan(IReadOnlyList<(string Source, string Dest, long Size)> files)
    {
        TotalFiles = files.Count;
        TotalBytes = files.Sum(f => f.Size);
        foreach (var f in files)
            _states[f.Source] = new TransferFileState(f.Source, f.Dest, f.Size, TransferFileStatus.Queued, 0);
        _clock.Start();
        Changed?.Invoke();
    }

    internal void FileStarted(string source, string dest, long size)
    {
        lock (_gate)
        {
            _currentFileName = Path.GetFileName(source);
            _currentFileBytesDone = 0;
            _currentFileSize = size;
        }

        _states[source] = new TransferFileState(source, dest, size, TransferFileStatus.InProgress, 0);
        Changed?.Invoke();
    }

    internal void FileProgress(string source, long fileBytesDone, long chunk)
    {
        lock (_gate)
        {
            _bytesDone += chunk;
            _currentFileBytesDone = fileBytesDone;
        }

        if (_states.TryGetValue(source, out var s))
            _states[source] = s with
            {
                Status = TransferFileStatus.InProgress,
                Percent = s.SizeBytes > 0 ? 100.0 * fileBytesDone / s.SizeBytes : 100,
            };
        Changed?.Invoke();
    }

    internal void FileDone(string source)
    {
        lock (_gate)
        {
            _filesDone++;
        }

        if (_states.TryGetValue(source, out var s))
            _states[source] = s with { Status = TransferFileStatus.Done, Percent = 100 };
        Changed?.Invoke();
    }

    internal void FileSkipped(string source, string reason)
    {
        lock (_gate)
        {
            _skipped.Add(new SkippedFile(source, reason));
        }

        if (_states.TryGetValue(source, out var s))
            _states[source] = s with { Status = TransferFileStatus.Skipped };
        Changed?.Invoke();
    }

    internal void Finished()
    {
        _complete = true;
        _clock.Stop();
        Changed?.Invoke();
    }
}

public static class TransferEngine
{
    public const string SkipReasonNewer = "same name, newer at destination";
    private const int ChunkSize = 1024 * 1024;

    private static readonly LocalFileSystemProvider _local = new();

    /// <summary>
    /// Starts copying/moving <paramref name="sourcePaths"/> (files or directories)
    /// into <paramref name="destinationDir"/> on a background task using the local
    /// file system. Behavior is identical to before this overload was provider-aware.
    /// </summary>
    public static TransferSession Start(
        IReadOnlyList<string> sourcePaths, string destinationDir, TransferMode mode)
        => Start(sourcePaths, _local, destinationDir, _local, mode);

    /// <summary>
    /// Provider-aware overload: copies/moves files from <paramref name="srcProvider"/>
    /// into <paramref name="destProvider"/>. Stream-copy via <c>OpenRead</c>/<c>OpenWrite</c>;
    /// <c>.part</c>+rename only when <c>dest.Capabilities.AtomicRename</c>; mtime copy only
    /// when <c>dest.Capabilities.PreservesMTime</c>; move = native <c>Rename</c> when same
    /// provider instance and <c>CanRename</c>, else copy+delete.
    /// </summary>
    public static TransferSession Start(
        IReadOnlyList<string> sourcePaths,
        IFileSystemProvider srcProvider,
        string destinationDir,
        IFileSystemProvider destProvider,
        TransferMode mode)
    {
        var session = new TransferSession(mode, destinationDir);
        session.Completion = Task.Run(() => Run(session, sourcePaths, srcProvider, destinationDir, destProvider, mode));
        return session;
    }

    private static void Run(
        TransferSession session,
        IReadOnlyList<string> sourcePaths,
        IFileSystemProvider srcProvider,
        string destinationDir,
        IFileSystemProvider destProvider,
        TransferMode mode)
    {
        try
        {
            var files = new List<(string Source, string Dest, long Size)>();
            var dirPairs = new List<(string Source, string Dest)>();
            var srcSep = srcProvider.Capabilities.Separator;
            var destSep = destProvider.Capabilities.Separator;

            foreach (var source in sourcePaths)
            {
                if (srcProvider.DirectoryExists(source))
                {
                    var srcLeaf = ProviderLeaf(source, srcSep);
                    var destRoot = ProviderCombine(destinationDir, srcLeaf, destSep);
                    dirPairs.Add((source, destRoot));
                    foreach (var entry in srcProvider.EnumerateRecursive(source))
                    {
                        var relPath = ProviderRelativePath(source, entry.FullPath, srcSep);
                        var destPath = ProviderCombineRel(destRoot, relPath, srcSep, destSep);
                        if (entry.IsDirectory)
                            dirPairs.Add((entry.FullPath, destPath));
                        else
                            files.Add((entry.FullPath, destPath, entry.SizeBytes));
                    }
                }
                else if (srcProvider.FileExists(source))
                {
                    var srcLeaf = ProviderLeaf(source, srcSep);
                    var destPath = ProviderCombine(destinationDir, srcLeaf, destSep);
                    var stat = srcProvider.Stat(source);
                    files.Add((source, destPath, stat?.SizeBytes ?? 0));
                }
            }

            session.Plan(files);

            // Create destination directories (in order, so parents come before children).
            foreach (var (_, dest) in dirPairs)
            {
                session.Token.ThrowIfCancellationRequested();
                if (!destProvider.DirectoryExists(dest))
                {
                    var parent = ProviderParent(dest, destSep);
                    var name = ProviderLeaf(dest, destSep);
                    if (parent is not null && name.Length > 0)
                        destProvider.CreateDirectory(parent, name);
                }
            }

            foreach (var (source, dest, size) in files)
            {
                session.WaitIfPaused();
                session.Token.ThrowIfCancellationRequested();

                var srcStat = srcProvider.Stat(source);
                var destStat = destProvider.Stat(dest);
                if (destStat is not null && srcStat is not null
                    && destStat.ModifiedUtc >= srcStat.ModifiedUtc)
                {
                    session.FileSkipped(source, SkipReasonNewer);
                    continue;
                }

                session.FileStarted(source, dest, size);

                // Move shortcut: same provider and provider supports native rename.
                if (mode == TransferMode.Move && ReferenceEquals(srcProvider, destProvider)
                    && srcProvider.Capabilities.CanRename)
                {
                    var destLeaf = ProviderLeaf(dest, destSep);
                    var destParent = ProviderParent(dest, destSep) ?? destinationDir;
                    // Rename moves within the same parent; for cross-dir we need to move to a temp
                    // name if the provider doesn't have a full move. However IFileSystemProvider.Rename
                    // only renames the leaf. We fall back to stream copy+delete for cross-directory moves.
                    var srcParent = ProviderParent(source, srcSep);
                    if (srcParent == destParent)
                    {
                        srcProvider.Rename(source, destLeaf);
                        session.FileProgress(source, size, size);
                        session.FileDone(source);
                        continue;
                    }
                    // Cross-directory within same provider: fall through to stream copy+delete.
                }

                var srcMtime = srcStat?.ModifiedUtc ?? DateTime.UtcNow;
                CopyFile(session, source, dest, srcMtime, srcProvider, destProvider);
                if (mode == TransferMode.Move)
                    srcProvider.Delete(source, toTrash: false);
                session.FileDone(source);
            }

            if (mode == TransferMode.Move)
            {
                // Depth-first so children go before parents; only empty dirs are removed.
                foreach (var (dirSource, _) in dirPairs.OrderByDescending(p => p.Source.Length))
                {
                    if (srcProvider.DirectoryExists(dirSource)
                        && !srcProvider.EnumerateRecursive(dirSource).Any())
                        srcProvider.Delete(dirSource, toTrash: false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            session.Finished();
        }
    }

    private static void CopyFile(
        TransferSession session,
        string source,
        string dest,
        DateTime sourceMtimeUtc,
        IFileSystemProvider srcProvider,
        IFileSystemProvider destProvider)
    {
        var useAtomicRename = destProvider.Capabilities.AtomicRename;
        var writePath = useAtomicRename ? dest + ".part" : dest;
        try
        {
            using (var input = srcProvider.OpenRead(source))
            using (var output = destProvider.OpenWrite(writePath))
            {
                var buffer = new byte[ChunkSize];
                long total = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    session.WaitIfPaused();
                    session.Token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    total += read;
                    session.FileProgress(source, total, read);
                }
            }

            if (useAtomicRename)
            {
                // Remove a stale destination so the rename does not fail on providers that
                // do not support overwriting moves (e.g. the local provider uses File.Move
                // without the overwrite flag).
                if (destProvider.FileExists(dest))
                    destProvider.Delete(dest, toTrash: false);
                destProvider.Rename(writePath, ProviderLeaf(dest, destProvider.Capabilities.Separator));
            }

            if (destProvider.Capabilities.PreservesMTime)
                destProvider.SetLastWriteTimeUtc(dest, sourceMtimeUtc);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (useAtomicRename && destProvider.FileExists(writePath))
                destProvider.Delete(writePath, toTrash: false);
            throw;
        }
    }

    // ── Provider path helpers ────────────────────────────────────────────────

    private static string ProviderLeaf(string path, char sep)
    {
        var trimmed = path.TrimEnd(sep);
        var idx = trimmed.LastIndexOf(sep);
        return idx < 0 ? trimmed : trimmed[(idx + 1)..];
    }

    private static string? ProviderParent(string path, char sep)
    {
        var trimmed = path.TrimEnd(sep);
        var idx = trimmed.LastIndexOf(sep);
        if (idx < 0)
            return null;
        var parent = trimmed[..idx];
        // Preserve a bare separator root (e.g. "/" on unix or in-memory).
        return parent.Length == 0 ? sep.ToString() : parent;
    }

    private static string ProviderCombine(string parent, string name, char sep)
    {
        var p = parent.TrimEnd(sep);
        return p.Length == 0 ? sep + name : p + sep + name;
    }

    /// <summary>
    /// Returns the relative portion of <paramref name="fullPath"/> below <paramref name="basePath"/>
    /// using the source separator. E.g. base="/a" full="/a/b/c" sep='/' → "b/c".
    /// </summary>
    private static string ProviderRelativePath(string basePath, string fullPath, char sep)
    {
        var b = basePath.TrimEnd(sep);
        if (fullPath.StartsWith(b + sep, StringComparison.Ordinal))
            return fullPath[(b.Length + 1)..];
        return fullPath;
    }

    /// <summary>
    /// Appends a source-relative path segment onto a dest base, translating separators.
    /// E.g. destBase="/dst", relPath="sub/file.txt", srcSep='/', destSep='/' → "/dst/sub/file.txt".
    /// When source and dest use different separators the rel path segments are split and rejoined.
    /// </summary>
    private static string ProviderCombineRel(string destBase, string relPath, char srcSep, char destSep)
    {
        var segments = relPath.Split(srcSep, StringSplitOptions.RemoveEmptyEntries);
        var result = destBase.TrimEnd(destSep);
        foreach (var seg in segments)
            result = result + destSep + seg;
        return result;
    }
}
