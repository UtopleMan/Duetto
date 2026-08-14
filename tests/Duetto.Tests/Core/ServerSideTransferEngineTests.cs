using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

public class ServerSideTransferEngineTests
{
    private sealed class BackendProvider(InMemoryFileSystemProvider store, string backendKey)
        : IFileSystemProvider, IBackendIdentity, IServerSideCopy
    {
        public bool MoveCalled;
        public bool OpenReadCalled;
        public bool OpenWriteCalled;
        public bool FailNextMove;
        public bool ServerSideCopyCalled;
        public bool ServerSideCopySupported = true;

        public bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token)
        {
            ServerSideCopyCalled = true;
            if (!ServerSideCopySupported)
                return false;
            using var src = store.OpenRead(source);
            using var ms = new MemoryStream();
            src.CopyTo(ms);
            var bytes = ms.ToArray();
            using (var w = store.OpenWrite(dest)) w.Write(bytes);
            onBytesCopied(bytes.Length);
            return true;
        }

        public string? BackendKey(string path) => path is "" or "/" ? null : backendKey;
        public FileSystemCapabilities Capabilities => store.Capabilities;
        public IReadOnlyList<FileEntry> List(string p) => store.List(p);
        public bool DirectoryExists(string p) => store.DirectoryExists(p);
        public bool FileExists(string p) => store.FileExists(p);
        public FileEntry? Stat(string p) => store.Stat(p);
        public string CreateDirectory(string parent, string name) => store.CreateDirectory(parent, name);
        public string CreateFile(string parent, string name) => store.CreateFile(parent, name);
        public string Rename(string p, string n) => store.Rename(p, n);
        public void ReplaceFile(string f, string t) => store.ReplaceFile(f, t);
        public void Delete(string p, bool trash) => store.Delete(p, trash);
        public void SetLastWriteTimeUtc(string p, DateTime u) => store.SetLastWriteTimeUtc(p, u);
        public IEnumerable<FileEntry> EnumerateRecursive(string p) => store.EnumerateRecursive(p);
        public VolumeInfo? VolumeFor(string p) => null;

        public void Move(string from, string to)
        {
            if (FailNextMove) { FailNextMove = false; throw new IOException("rename refused"); }
            MoveCalled = true; store.Move(from, to);
        }
        public Stream OpenRead(string p) { OpenReadCalled = true; return store.OpenRead(p); }
        public Stream OpenWrite(string p) { OpenWriteCalled = true; return store.OpenWrite(p); }
    }

    private static (BackendProvider, BackendProvider, InMemoryFileSystemProvider) SameBackendPair(string key = "smb://h/s")
    {
        var store = new InMemoryFileSystemProvider();
        store.CreateDirectory("/", "src");
        store.CreateDirectory("/", "dst");
        return (new BackendProvider(store, key), new BackendProvider(store, key), store);
    }

    private static void Seed(IFileSystemProvider fs, string dir, string name, string text)
    {
        var full = fs.CreateFile(dir, name);
        using var w = fs.OpenWrite(full);
        w.Write(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private static string ReadText(IFileSystemProvider fs, string path)
    {
        using var s = fs.OpenRead(path); using var ms = new MemoryStream();
        s.CopyTo(ms); return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public async Task Move_across_same_backend_instances_uses_native_Move_not_streaming()
    {
        var (src, dst, store) = SameBackendPair();
        Seed(src, "/src", "a.txt", "server move");

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Move);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.True(src.MoveCalled, "must use native Move across same-backend instances");
        Assert.False(src.OpenReadCalled, "must not stream through the client");
        Assert.False(src.FileExists("/src/a.txt"));
        Assert.Equal("server move", ReadText(store, "/dst/a.txt"));
    }

    [Fact]
    public async Task Move_falls_back_when_native_Move_throws_nonfatal()
    {
        var (src, dst, store) = SameBackendPair();
        Seed(src, "/src", "a.txt", "fallback move");
        src.FailNextMove = true;

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Move);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.False(src.MoveCalled, "native Move threw, never marked success");
        Assert.False(src.FileExists("/src/a.txt"), "source removed after fallback move");
        Assert.Equal("fallback move", ReadText(store, "/dst/a.txt"));
    }

    [Fact]
    public async Task Move_across_different_backends_streams()
    {
        var storeA = new InMemoryFileSystemProvider(); storeA.CreateDirectory("/", "src");
        var storeB = new InMemoryFileSystemProvider(); storeB.CreateDirectory("/", "dst");
        var src = new BackendProvider(storeA, "smb://h/s");
        var dst = new BackendProvider(storeB, "smb://other/s");
        Seed(src, "/src", "a.txt", "x");

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Move);
        await session.Completion;

        Assert.False(src.MoveCalled);
        Assert.True(src.OpenReadCalled);
    }

    [Fact]
    public async Task Copy_across_same_backend_uses_server_side_copy_not_streaming()
    {
        var (src, dst, store) = SameBackendPair();
        Seed(src, "/src", "a.txt", "offloaded copy");

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Copy);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.True(src.ServerSideCopyCalled, "must attempt server-side copy");
        Assert.False(src.OpenReadCalled, "must not stream through the client");
        Assert.Equal("offloaded copy", ReadText(store, "/dst/a.txt"));
    }

    [Fact]
    public async Task Copy_falls_back_to_stream_when_offload_unsupported()
    {
        var (src, dst, store) = SameBackendPair();
        Seed(src, "/src", "a.txt", "streamed copy");
        src.ServerSideCopySupported = false;

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Copy);
        await session.Completion;

        Assert.True(src.ServerSideCopyCalled, "offload attempted");
        Assert.True(src.OpenReadCalled, "then streamed");
        Assert.Equal("streamed copy", ReadText(store, "/dst/a.txt"));
    }

    [Fact]
    public async Task Copy_across_different_backends_does_not_attempt_offload()
    {
        var storeA = new InMemoryFileSystemProvider(); storeA.CreateDirectory("/", "src");
        var storeB = new InMemoryFileSystemProvider(); storeB.CreateDirectory("/", "dst");
        var src = new BackendProvider(storeA, "smb://h/s");
        var dst = new BackendProvider(storeB, "smb://other/s");
        Seed(src, "/src", "a.txt", "x");

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Copy);
        await session.Completion;

        Assert.False(src.ServerSideCopyCalled);
        Assert.True(src.OpenReadCalled);
    }
}
