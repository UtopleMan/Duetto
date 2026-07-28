using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

/// <summary>
/// Cross-provider transfer tests: exercises the provider-aware TransferEngine.Start overload
/// through InMemoryFileSystemProvider so no real disk or network is needed.
/// Covers: file copy content round-trip, move via native rename (same provider),
/// move via copy+delete (cross-provider), capability gating (no SetLastWriteTimeUtc
/// when PreservesMTime is false), and directory recursion.
/// </summary>
public class CrossProviderTransferTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static InMemoryFileSystemProvider MakeMemFs(bool preservesMTime = true, bool atomicRename = true)
        => new() { Capabilities = new FileSystemCapabilities
        {
            CanRename           = true,
            CanCreateEmptyDir   = true,
            CanCreateFile       = true,
            CanDelete           = true,
            HasTrash            = false,
            HasPermissions      = false,
            PreservesMTime      = preservesMTime,
            AtomicRename        = atomicRename,
            CanWatch            = false,
            ReportsCapacity     = false,
            SupportsSearch      = true,
            CaseSensitive       = true,
            Separator           = '/',
        }};

    /// <summary>Seeds a file into an in-memory provider (creates parent dirs as needed).</summary>
    private static void Seed(InMemoryFileSystemProvider fs, string path, string content,
        DateTime? mtime = null)
    {
        var lastSlash = path.LastIndexOf('/');
        var parent    = lastSlash <= 0 ? "/" : path[..lastSlash];
        var name      = path[(lastSlash + 1)..];

        // Ensure parent directories exist.
        if (!fs.DirectoryExists(parent))
        {
            var segments = parent.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var cur = "/";
            foreach (var seg in segments)
            {
                var child = cur == "/" ? "/" + seg : cur + "/" + seg;
                if (!fs.DirectoryExists(child))
                    fs.CreateDirectory(cur, seg);
                cur = child;
            }
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var full  = fs.CreateFile(parent, name);
        using var w = fs.OpenWrite(full);
        w.Write(bytes);
        if (mtime.HasValue)
            fs.SetLastWriteTimeUtc(full, mtime.Value);
    }

    private static string ReadText(IFileSystemProvider fs, string path)
    {
        using var s  = fs.OpenRead(path);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task File_copy_content_round_trip_inmemory_to_inmemory()
    {
        var src = MakeMemFs();
        var dst = MakeMemFs();
        src.CreateDirectory("/", "s");
        dst.CreateDirectory("/", "d");
        Seed(src, "/s/hello.txt", "hello world");

        var session = TransferEngine.Start(["/s/hello.txt"], src, "/d", dst, TransferMode.Copy);
        await session.Completion;

        var snap = session.Snapshot();
        Assert.True(snap.IsComplete);
        Assert.Equal(1, snap.TotalFiles);
        Assert.Equal(1, snap.FilesDone);
        Assert.Equal("hello world", ReadText(dst, "/d/hello.txt"));
    }

    [Fact]
    public async Task File_copy_content_round_trip_local_to_inmemory()
    {
        using var srcDir = new TempDir();
        srcDir.File("msg.txt", "from local");

        var dst  = MakeMemFs();
        dst.CreateDirectory("/", "out");
        var srcProvider = new LocalFileSystemProvider();

        var session = TransferEngine.Start(
            [Path.Combine(srcDir.Path, "msg.txt")], srcProvider, "/out", dst, TransferMode.Copy);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.Equal("from local", ReadText(dst, "/out/msg.txt"));
    }

    [Fact]
    public async Task Move_with_same_provider_uses_copy_delete_cross_directory()
    {
        // IFileSystemProvider.Rename only renames the leaf within its current parent,
        // so cross-directory moves always fall through to copy+delete — even with the
        // same provider instance.  After the move, source is gone and dest has content.
        var fs = MakeMemFs();
        fs.CreateDirectory("/", "s");
        fs.CreateDirectory("/", "d");
        Seed(fs, "/s/a.txt", "moved content");

        var session = TransferEngine.Start(["/s/a.txt"], fs, "/d", fs, TransferMode.Move);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.False(fs.FileExists("/s/a.txt"));
        Assert.Equal("moved content", ReadText(fs, "/d/a.txt"));
    }

    [Fact]
    public async Task Move_via_copy_and_delete_across_providers()
    {
        var src = MakeMemFs();
        var dst = MakeMemFs();
        src.CreateDirectory("/", "s");
        dst.CreateDirectory("/", "d");
        Seed(src, "/s/data.txt", "cross move");

        var session = TransferEngine.Start(["/s/data.txt"], src, "/d", dst, TransferMode.Move);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.False(src.FileExists("/s/data.txt"));
        Assert.Equal("cross move", ReadText(dst, "/d/data.txt"));
    }

    [Fact]
    public async Task Mtime_not_set_when_PreservesMTime_false()
    {
        var src = MakeMemFs();
        src.CreateDirectory("/", "s");
        Seed(src, "/s/f.txt", "content", DateTime.UtcNow.AddHours(-1));

        // Wrap an in-memory provider with a spy that tracks SetLastWriteTimeUtc calls.
        var innerDst   = MakeMemFs(preservesMTime: false);
        innerDst.CreateDirectory("/", "d");
        var spy = new MTimeCallSpy(innerDst);

        var session = TransferEngine.Start(["/s/f.txt"], src, "/d", spy, TransferMode.Copy);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.False(spy.MTimeWasSet, "SetLastWriteTimeUtc must not be called when PreservesMTime is false");
        Assert.Equal("content", ReadText(innerDst, "/d/f.txt"));
    }

    [Fact]
    public async Task Directory_recursion_copy_inmemory_to_inmemory()
    {
        var src = MakeMemFs();
        var dst = MakeMemFs();
        dst.CreateDirectory("/", "out");

        src.CreateDirectory("/", "tree");
        src.CreateDirectory("/tree", "sub");
        Seed(src, "/tree/root.txt", "root file");
        Seed(src, "/tree/sub/child.txt", "child file");

        var session = TransferEngine.Start(["/tree"], src, "/out", dst, TransferMode.Copy);
        await session.Completion;

        var snap = session.Snapshot();
        Assert.True(snap.IsComplete);
        Assert.Equal(2, snap.TotalFiles);
        Assert.Equal(2, snap.FilesDone);
        Assert.True(dst.DirectoryExists("/out/tree"));
        Assert.True(dst.DirectoryExists("/out/tree/sub"));
        Assert.Equal("root file",  ReadText(dst, "/out/tree/root.txt"));
        Assert.Equal("child file", ReadText(dst, "/out/tree/sub/child.txt"));
    }

    [Fact]
    public async Task Move_directory_removes_source_tree_across_providers()
    {
        var src = MakeMemFs();
        var dst = MakeMemFs();
        dst.CreateDirectory("/", "out");

        src.CreateDirectory("/", "dir");
        src.CreateDirectory("/dir", "inner");
        Seed(src, "/dir/inner/file.txt", "deep content");

        var session = TransferEngine.Start(["/dir"], src, "/out", dst, TransferMode.Move);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.False(src.DirectoryExists("/dir"));
        Assert.Equal("deep content", ReadText(dst, "/out/dir/inner/file.txt"));
    }

    [Fact]
    public async Task Skips_newer_destination_cross_provider()
    {
        var src = MakeMemFs();
        var dst = MakeMemFs();
        src.CreateDirectory("/", "s");
        dst.CreateDirectory("/", "d");

        Seed(src, "/s/f.txt", "old-source", DateTime.UtcNow.AddDays(-2));
        Seed(dst, "/d/f.txt", "newer-dest", DateTime.UtcNow);

        var session = TransferEngine.Start(["/s/f.txt"], src, "/d", dst, TransferMode.Copy);
        await session.Completion;

        var snap = session.Snapshot();
        Assert.Equal(1, snap.FilesSkipped);
        Assert.Equal(0, snap.FilesDone);
        var skipped = Assert.Single(snap.Skipped);
        Assert.Equal(TransferEngine.SkipReasonNewer, skipped.Reason);
        Assert.Equal("newer-dest", ReadText(dst, "/d/f.txt"));
    }

    [Fact]
    public async Task Same_provider_move_cross_directory_uses_native_Move()
    {
        // A single in-memory provider wrapped in a spy so we can confirm Move() was called.
        var inner = MakeMemFs();
        inner.CreateDirectory("/", "src");
        inner.CreateDirectory("/", "dst");
        Seed(inner, "/src/file.txt", "native move content");

        var spy = new MoveCallSpy(inner);

        var session = TransferEngine.Start(["/src/file.txt"], spy, "/dst", spy, TransferMode.Move);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.True(spy.MoveWasCalled, "TransferEngine must use provider.Move for same-provider cross-directory moves");
        Assert.False(inner.FileExists("/src/file.txt"), "source must be gone");
        Assert.Equal("native move content", ReadText(inner, "/dst/file.txt"));
    }

    // ── spy wrappers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Thin forwarding wrapper around an <see cref="IFileSystemProvider"/> that
    /// records whether <see cref="SetLastWriteTimeUtc"/> was ever called.
    /// Used to verify capability gating without subclassing the sealed
    /// <see cref="InMemoryFileSystemProvider"/>.
    /// </summary>
    private sealed class MTimeCallSpy(IFileSystemProvider inner) : IFileSystemProvider
    {
        public bool MTimeWasSet { get; private set; }

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
        public Stream OpenRead(string path)                        => inner.OpenRead(path);
        public Stream OpenWrite(string path)                       => inner.OpenWrite(path);
        public IEnumerable<FileEntry> EnumerateRecursive(string path) => inner.EnumerateRecursive(path);
        public VolumeInfo? VolumeFor(string path)                  => inner.VolumeFor(path);

        public void SetLastWriteTimeUtc(string path, DateTime utc)
        {
            MTimeWasSet = true;
            inner.SetLastWriteTimeUtc(path, utc);
        }
    }

    /// <summary>
    /// Thin forwarding wrapper that records whether <see cref="Move"/> was invoked.
    /// Used to verify the TransferEngine takes the native-move path for same-provider moves.
    /// </summary>
    private sealed class MoveCallSpy(IFileSystemProvider inner) : IFileSystemProvider
    {
        public bool MoveWasCalled { get; private set; }

        public FileSystemCapabilities Capabilities => inner.Capabilities;
        public IReadOnlyList<FileEntry> List(string path)          => inner.List(path);
        public bool DirectoryExists(string path)                   => inner.DirectoryExists(path);
        public bool FileExists(string path)                        => inner.FileExists(path);
        public FileEntry? Stat(string path)                        => inner.Stat(path);
        public string CreateDirectory(string parent, string name)  => inner.CreateDirectory(parent, name);
        public string CreateFile(string parent, string name)       => inner.CreateFile(parent, name);
        public string Rename(string fullPath, string newName)      => inner.Rename(fullPath, newName);
        public void ReplaceFile(string from, string to)            => inner.ReplaceFile(from, to);
        public void Delete(string path, bool toTrash)              => inner.Delete(path, toTrash);
        public Stream OpenRead(string path)                        => inner.OpenRead(path);
        public Stream OpenWrite(string path)                       => inner.OpenWrite(path);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => inner.SetLastWriteTimeUtc(path, utc);
        public IEnumerable<FileEntry> EnumerateRecursive(string path) => inner.EnumerateRecursive(path);
        public VolumeInfo? VolumeFor(string path)                  => inner.VolumeFor(path);

        public void Move(string fromPath, string toPath)
        {
            MoveWasCalled = true;
            inner.Move(fromPath, toPath);
        }
    }
}
