using System.Collections.Concurrent;
using System.Diagnostics;

namespace Duet.Core.Operations;

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

    /// <summary>
    /// Starts copying/moving <paramref name="sourcePaths"/> (files or directories)
    /// into <paramref name="destinationDir"/> on a background task.
    /// Conflict rule: destination file with the same relative name and an equal or
    /// newer mtime is skipped; an older one is overwritten.
    /// </summary>
    public static TransferSession Start(
        IReadOnlyList<string> sourcePaths, string destinationDir, TransferMode mode)
    {
        var session = new TransferSession(mode, destinationDir);
        session.Completion = Task.Run(() => Run(session, sourcePaths, destinationDir, mode));
        return session;
    }

    private static void Run(
        TransferSession session, IReadOnlyList<string> sourcePaths, string destinationDir, TransferMode mode)
    {
        try
        {
            var files = new List<(string Source, string Dest, long Size)>();
            var dirPairs = new List<(string Source, string Dest)>();
            foreach (var source in sourcePaths)
            {
                if (Directory.Exists(source))
                {
                    var destRoot = Path.Combine(destinationDir, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
                    dirPairs.Add((source, destRoot));
                    foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                        dirPairs.Add((dir, Path.Combine(destRoot, Path.GetRelativePath(source, dir))));
                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                        files.Add((file, Path.Combine(destRoot, Path.GetRelativePath(source, file)), new FileInfo(file).Length));
                }
                else if (File.Exists(source))
                {
                    files.Add((source, Path.Combine(destinationDir, Path.GetFileName(source)), new FileInfo(source).Length));
                }
            }

            session.Plan(files);

            foreach (var (_, dest) in dirPairs)
            {
                session.Token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(dest);
            }

            foreach (var (source, dest, size) in files)
            {
                session.WaitIfPaused();
                session.Token.ThrowIfCancellationRequested();

                var srcInfo = new FileInfo(source);
                var destInfo = new FileInfo(dest);
                if (destInfo.Exists && destInfo.LastWriteTimeUtc >= srcInfo.LastWriteTimeUtc)
                {
                    session.FileSkipped(source, SkipReasonNewer);
                    continue;
                }

                session.FileStarted(source, dest, size);
                CopyFile(session, source, dest, srcInfo.LastWriteTimeUtc);
                if (mode == TransferMode.Move)
                    File.Delete(source);
                session.FileDone(source);
            }

            if (mode == TransferMode.Move)
            {
                // Depth-first so children go before parents; only empty dirs are removed.
                foreach (var (dirSource, _) in dirPairs.OrderByDescending(p => p.Source.Length))
                {
                    if (Directory.Exists(dirSource) && !Directory.EnumerateFileSystemEntries(dirSource).Any())
                        Directory.Delete(dirSource);
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

    private static void CopyFile(TransferSession session, string source, string dest, DateTime sourceMtimeUtc)
    {
        var part = dest + ".part";
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = File.Create(part))
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

            File.Move(part, dest, overwrite: true);
            File.SetLastWriteTimeUtc(dest, sourceMtimeUtc);
        }
        catch
        {
            if (File.Exists(part))
                File.Delete(part);
            throw;
        }
    }
}
