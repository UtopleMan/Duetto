using Duetto.Core.FileSystem;
using Duetto.Core.Operations;

namespace Duetto.Tests.Core;

public class TransferEngineTests : IDisposable
{
    private readonly TempDir _src = new();
    private readonly TempDir _dst = new();

    public void Dispose()
    {
        _src.Dispose();
        _dst.Dispose();
    }

    [Fact]
    public async Task Copies_tree_and_reports_progress()
    {
        _src.File("a.txt", new string('a', 5000));
        _src.File("nested/b.txt", new string('b', 3000));

        using var session = TransferEngine.Start([_src.Path], _dst.Path, TransferMode.Copy);
        await session.Completion;

        var snap = session.Snapshot();
        Assert.True(snap.IsComplete);
        Assert.Equal(2, snap.TotalFiles);
        Assert.Equal(2, snap.FilesDone);
        Assert.Equal(8000, snap.BytesDone);

        var root = Path.GetFileName(_src.Path);
        Assert.Equal(new string('a', 5000), File.ReadAllText(Path.Combine(_dst.Path, root, "a.txt")));
        Assert.Equal(new string('b', 3000), File.ReadAllText(Path.Combine(_dst.Path, root, "nested", "b.txt")));
    }

    [Fact]
    public async Task Skips_newer_destination_and_lists_reason()
    {
        var old = DateTime.UtcNow.AddDays(-2);
        _src.File("keep.txt", "source-old", old);
        _dst.File("keep.txt", "dest-newer", DateTime.UtcNow);
        _src.File("fresh.txt", "copied");

        using var session = TransferEngine.Start(
            [Path.Combine(_src.Path, "keep.txt"), Path.Combine(_src.Path, "fresh.txt")],
            _dst.Path, TransferMode.Copy);
        await session.Completion;

        var snap = session.Snapshot();
        Assert.Equal(1, snap.FilesSkipped);
        Assert.Equal(1, snap.FilesDone);
        var skipped = Assert.Single(snap.Skipped);
        Assert.Equal(TransferEngine.SkipReasonNewer, skipped.Reason);
        Assert.Equal("dest-newer", File.ReadAllText(Path.Combine(_dst.Path, "keep.txt")));
        Assert.Equal("copied", File.ReadAllText(Path.Combine(_dst.Path, "fresh.txt")));
    }

    [Fact]
    public async Task Overwrites_older_destination()
    {
        _src.File("f.txt", "newer-source", DateTime.UtcNow);
        _dst.File("f.txt", "older-dest", DateTime.UtcNow.AddDays(-1));

        using var session = TransferEngine.Start(
            [Path.Combine(_src.Path, "f.txt")], _dst.Path, TransferMode.Copy);
        await session.Completion;

        Assert.Equal("newer-source", File.ReadAllText(Path.Combine(_dst.Path, "f.txt")));
        Assert.Equal(0, session.Snapshot().FilesSkipped);
    }

    [Fact]
    public async Task Move_removes_source_tree()
    {
        _src.File("m/inner.txt", "content");
        using var session = TransferEngine.Start(
            [Path.Combine(_src.Path, "m")], _dst.Path, TransferMode.Move);
        await session.Completion;

        Assert.False(Directory.Exists(Path.Combine(_src.Path, "m")));
        Assert.Equal("content", File.ReadAllText(Path.Combine(_dst.Path, "m", "inner.txt")));
    }

    [Fact]
    public async Task Cancel_stops_and_leaves_no_partial_files()
    {
        // Big enough that cancellation lands mid-copy.
        var big = new string('x', 20 * 1024 * 1024);
        _src.File("big1.bin", big);
        _src.File("big2.bin", big);

        using var session = TransferEngine.Start([_src.Path], _dst.Path, TransferMode.Copy);
        session.Cancel();
        await session.Completion;

        var snap = session.Snapshot();
        Assert.True(snap.IsCancelled);
        var root = Path.Combine(_dst.Path, Path.GetFileName(_src.Path));
        if (Directory.Exists(root))
            Assert.Empty(Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Pause_halts_progress_until_resume()
    {
        var big = new string('x', 30 * 1024 * 1024);
        _src.File("big.bin", big);

        using var session = TransferEngine.Start([Path.Combine(_src.Path, "big.bin")], _dst.Path, TransferMode.Copy);
        session.Pause();
        await Task.Delay(150);
        var frozen = session.Snapshot().BytesDone;
        await Task.Delay(150);
        Assert.Equal(frozen, session.Snapshot().BytesDone);

        session.Resume();
        await session.Completion;
        Assert.True(session.Snapshot().IsComplete);
        Assert.Equal(big.Length, session.Snapshot().BytesDone);
    }

    /// <summary>
    /// Deterministic: the source provider's read stream signals on its SECOND Read call
    /// and then blocks. The engine's chunk loop is Read → pause-check → cancel-check →
    /// Write → progress, so at the signal the first chunk has already been written to
    /// the .part file. We then cancel and release the blocked read; the next
    /// cancellation check throws mid-copy and the .part file must be cleaned up.
    /// </summary>
    [Fact]
    public async Task Cancel_mid_copy_cleans_up_part_file()
    {
        // 4 chunks of 1 MB — the gate fires on the second chunk read, so the copy
        // can never complete before we cancel.
        var big = new string('z', 4 * 1024 * 1024);
        _src.File("big.bin", big);

        var midCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);
        var local = new LocalFileSystemProvider();
        var gatedSrc = new GatedReadProvider(local, midCopy, release);

        using var session = TransferEngine.Start(
            [Path.Combine(_src.Path, "big.bin")], gatedSrc, _dst.Path, local, TransferMode.Copy);
        try
        {
            // Deterministic gate: resolves exactly when the engine is mid-copy
            // (chunk 1 written, chunk 2 read in flight and blocked).
            await midCopy.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(File.Exists(Path.Combine(_dst.Path, "big.bin.part")),
                ".part file must exist mid-copy");
            session.Cancel();
        }
        finally
        {
            // Always unblock the worker, even if an assert above failed.
            release.Set();
        }

        await session.Completion;

        Assert.True(session.Snapshot().IsCancelled);
        // No orphaned .part files may remain.
        Assert.Empty(Directory.EnumerateFiles(_dst.Path, "*.part", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Forwards everything to the inner provider but wraps <see cref="OpenRead"/> streams
    /// in a <see cref="GatedReadStream"/> so a test can deterministically catch the
    /// transfer engine mid-copy.
    /// </summary>
    private sealed class GatedReadProvider(
        IFileSystemProvider inner, TaskCompletionSource midCopy, ManualResetEventSlim release)
        : IFileSystemProvider
    {
        public FileSystemCapabilities Capabilities => inner.Capabilities;
        public IReadOnlyList<FileEntry> List(string path)          => inner.List(path);
        public bool DirectoryExists(string path)                   => inner.DirectoryExists(path);
        public bool FileExists(string path)                        => inner.FileExists(path);
        public FileEntry? Stat(string path)                        => inner.Stat(path);
        public string CreateDirectory(string parent, string name)  => inner.CreateDirectory(parent, name);
        public string CreateFile(string parent, string name)       => inner.CreateFile(parent, name);
        public string Rename(string fullPath, string newName)      => inner.Rename(fullPath, newName);
        public void Move(string fromPath, string toPath)           => inner.Move(fromPath, toPath);
        public void ReplaceFile(string from, string to)            => inner.ReplaceFile(from, to);
        public void Delete(string path, bool toTrash)              => inner.Delete(path, toTrash);
        public Stream OpenWrite(string path)                       => inner.OpenWrite(path);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => inner.SetLastWriteTimeUtc(path, utc);
        public IEnumerable<FileEntry> EnumerateRecursive(string path) => inner.EnumerateRecursive(path);
        public VolumeInfo? VolumeFor(string path)                  => inner.VolumeFor(path);

        public Stream OpenRead(string path) =>
            new GatedReadStream(inner.OpenRead(path), midCopy, release);
    }

    /// <summary>
    /// Read-only stream wrapper: the second <see cref="Read"/> call completes
    /// <paramref name="midCopy"/> and blocks on <paramref name="release"/> before
    /// delegating, freezing the copy loop at a precisely known point.
    /// </summary>
    private sealed class GatedReadStream(
        Stream inner, TaskCompletionSource midCopy, ManualResetEventSlim release) : Stream
    {
        private int _reads;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Interlocked.Increment(ref _reads) == 2)
            {
                midCopy.TrySetResult();
                release.Wait();
            }

            return inner.Read(buffer, offset, count);
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
