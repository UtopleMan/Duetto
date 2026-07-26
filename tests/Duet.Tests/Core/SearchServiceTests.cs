using Duet.Core.Search;

namespace Duet.Tests.Core;

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
