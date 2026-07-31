using Duetto.Core.FileSystem;
using Duetto.Core.Search;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

public class SearchServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task Finds_nested_files_by_name()
    {
        _tmp.File("src/Views/MainWindow.axaml", "<Window/>");
        _tmp.File("src/Controls/FileGrid.axaml.cs", "class FileGrid {}");
        _tmp.File("readme.md", "docs");

        var stats = new SearchStats();
        var hits = new List<SearchHit>();
        await foreach (var hit in SearchService.Search(_tmp.Path, "axaml", includeContents: false, stats))
            hits.Add(hit);

        Assert.Equal(2, hits.Count);
        var window = Assert.Single(hits, h => h.Entry.Name == "MainWindow.axaml");
        Assert.Equal(Path.Combine("src", "Views"), window.RelativeFolder);
        Assert.True(stats.FilesScanned >= 3);
    }

    [Fact]
    public async Task Content_search_finds_text_and_skips_binary()
    {
        _tmp.File("notes.txt", "the treasure is buried here");
        File.WriteAllBytes(Path.Combine(_tmp.Path, "blob.bin"), [0, 1, 2, 0, 116, 114, 101, 97, 115, 117, 114, 101]);

        var stats = new SearchStats();
        var hits = new List<SearchHit>();
        await foreach (var hit in SearchService.Search(_tmp.Path, "treasure", includeContents: true, stats))
            hits.Add(hit);

        var hit0 = Assert.Single(hits);
        Assert.Equal("notes.txt", hit0.Entry.Name);
    }

    [Fact]
    public async Task Matches_directory_names()
    {
        _tmp.Dir("shipyard-assets");
        var stats = new SearchStats();
        var hits = new List<SearchHit>();
        await foreach (var hit in SearchService.Search(_tmp.Path, "shipyard", includeContents: false, stats))
            hits.Add(hit);
        Assert.Single(hits);
        Assert.True(hits[0].Entry.IsDirectory);
    }

    [Fact]
    public async Task Unreadable_subdirectory_does_not_abort_search()
    {
        if (OperatingSystem.IsWindows())
            return; // chmod-based denial is unix-only
        _tmp.File("visible/find-me.txt", "x");
        var locked = _tmp.Dir("locked");
        File.WriteAllText(Path.Combine(locked, "find-me-too.txt"), "x");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var stats = new SearchStats();
            var hits = new List<SearchHit>();
            await foreach (var hit in SearchService.Search(_tmp.Path, "find-me", includeContents: false, stats))
                hits.Add(hit);

            Assert.Contains(hits, h => h.Entry.Name == "find-me.txt");
        }
        finally
        {
            File.SetUnixFileMode(locked,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Cancellation_stops_enumeration()
    {
        for (var i = 0; i < 50; i++)
            _tmp.File($"dir{i}/file{i}.txt", "x");

        using var cts = new CancellationTokenSource();
        var stats = new SearchStats();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in SearchService.Search(_tmp.Path, "file", false, stats, cts.Token))
                cts.Cancel();
        });
    }
}

// Guards RelativeFolder when the search scope is the provider root "/" (localPath has a
// trailing separator) — the scopeBase-trimming edge case.
public class SearchServiceRootScopeTests
{
    [Fact]
    public async Task RelativeFolder_correct_when_scope_is_root()
    {
        var mem = new InMemoryFileSystemProvider
        {
            Capabilities = new Duetto.Core.FileSystem.FileSystemCapabilities
            {
                CanRename         = true,
                CanCreateEmptyDir = true,
                CanCreateFile     = true,
                CanDelete         = true,
                HasTrash          = false,
                HasPermissions    = false,
                PreservesMTime    = false,
                AtomicRename      = false,
                CanWatch          = false,
                ReportsCapacity   = false,
                SupportsSearch    = true,
                CaseSensitive     = true,
                Separator         = '/',
            }
        };

        var rootFile = mem.CreateFile("/", "root.txt");
        {
            using var w = mem.OpenWrite(rootFile);
            w.Write(System.Text.Encoding.UTF8.GetBytes("root-content"));
        }
        mem.CreateDirectory("/", "docs");
        var docsFile = mem.CreateFile("/docs", "guide.txt");
        {
            using var w = mem.OpenWrite(docsFile);
            w.Write(System.Text.Encoding.UTF8.GetBytes("docs-content"));
        }
        mem.CreateDirectory("/docs", "sub");
        var subFile = mem.CreateFile("/docs/sub", "index.txt");
        {
            using var w = mem.OpenWrite(subFile);
            w.Write(System.Text.Encoding.UTF8.GetBytes("sub-content"));
        }

        var reg = new Duetto.Core.FileSystem.FileSystemRegistry();
        reg.Register("mem", "host", mem);

        // Scope is the root with a trailing slash — this is the bug-trigger case.
        var scope = "mem://host/";

        var stats = new SearchStats();
        var hits  = new List<SearchHit>();
        await foreach (var hit in SearchService.Search(scope, ".txt", includeContents: false, stats, reg))
            hits.Add(hit);

        Assert.Equal(3, hits.Count);
        Assert.Single(hits, h => h.Entry.Name == "root.txt"  && h.RelativeFolder == "");
        Assert.Single(hits, h => h.Entry.Name == "guide.txt" && h.RelativeFolder == "docs");
        Assert.Single(hits, h => h.Entry.Name == "index.txt" && h.RelativeFolder == "docs/sub");
    }
}

public class SearchServiceProviderTests
{
    private static (FileSystemRegistry Registry, string Scope, InMemoryFileSystemProvider Mem)
        BuildMemRegistry(string rootPath, bool supportsSearch = true)
    {
        var mem = new InMemoryFileSystemProvider
        {
            Capabilities = new FileSystemCapabilities
            {
                CanRename         = true,
                CanCreateEmptyDir = true,
                CanCreateFile     = true,
                CanDelete         = true,
                HasTrash          = false,
                HasPermissions    = false,
                PreservesMTime    = true,
                AtomicRename      = false,
                CanWatch          = false,
                ReportsCapacity   = false,
                SupportsSearch    = supportsSearch,
                CaseSensitive     = true,
                Separator         = '/',
            }
        };
        var reg = new FileSystemRegistry();
        reg.Register("mem", "host", mem);
        SeedDir(mem, rootPath);
        var scope = "mem://host" + rootPath;
        return (reg, scope, mem);
    }

    private static void SeedDir(InMemoryFileSystemProvider mem, string path)
    {
        if (path == "/" || mem.DirectoryExists(path))
            return;
        var slash = path.LastIndexOf('/');
        var parent = slash <= 0 ? "/" : path[..slash];
        var name   = path[(slash + 1)..];
        SeedDir(mem, parent);
        if (!mem.DirectoryExists(path))
            mem.CreateDirectory(parent, name);
    }

    private static void SeedFile(InMemoryFileSystemProvider mem, string path, string content)
    {
        var slash  = path.LastIndexOf('/');
        var parent = slash <= 0 ? "/" : path[..slash];
        var name   = path[(slash + 1)..];
        SeedDir(mem, parent);
        var full = mem.CreateFile(parent, name);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        using var w = mem.OpenWrite(full);
        w.Write(bytes);
    }

    [Fact]
    public async Task Provider_name_match_finds_nested_files_via_registry()
    {
        var (reg, scope, mem) = BuildMemRegistry("/scope");
        SeedFile(mem, "/scope/src/Views/MainWindow.axaml", "<Window/>");
        SeedFile(mem, "/scope/src/Controls/FileGrid.axaml.cs", "class C {}");
        SeedFile(mem, "/scope/readme.md", "docs");

        var stats = new SearchStats();
        var hits  = new List<SearchHit>();
        await foreach (var hit in SearchService.Search(scope, "axaml", includeContents: false, stats, reg))
            hits.Add(hit);

        Assert.Equal(2, hits.Count);
        var window = Assert.Single(hits, h => h.Entry.Name == "MainWindow.axaml");
        Assert.Equal("src/Views", window.RelativeFolder);
        Assert.True(stats.FilesScanned >= 3);
    }

    [Fact]
    public async Task Provider_content_search_finds_text_in_memory()
    {
        var (reg, scope, mem) = BuildMemRegistry("/scope");
        SeedFile(mem, "/scope/notes.txt", "the treasure is buried here");
        SeedFile(mem, "/scope/other.txt", "nothing relevant");

        var stats = new SearchStats();
        var hits  = new List<SearchHit>();
        await foreach (var hit in SearchService.Search(scope, "treasure", includeContents: true, stats, reg))
            hits.Add(hit);

        var single = Assert.Single(hits);
        Assert.Equal("notes.txt", single.Entry.Name);
        Assert.Equal("", single.RelativeFolder);
    }

    [Fact]
    public async Task SupportsSearch_false_yields_no_results()
    {
        // Provider with SupportsSearch=false must return an empty sequence — no exception.
        var (reg, scope, mem) = BuildMemRegistry("/scope", supportsSearch: false);
        SeedFile(mem, "/scope/findme.txt", "target");

        var stats = new SearchStats();
        var hits  = new List<SearchHit>();
        await foreach (var hit in SearchService.Search(scope, "findme", includeContents: false, stats, reg))
            hits.Add(hit);

        Assert.Empty(hits);
        Assert.Equal(0, stats.FilesScanned);
        Assert.Equal(0, stats.Matches);
    }
}
