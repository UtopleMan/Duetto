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
}
