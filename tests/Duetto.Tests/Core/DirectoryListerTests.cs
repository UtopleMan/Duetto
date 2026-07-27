using Duetto.Core.FileSystem;

namespace Duetto.Tests.Core;

public class DirectoryListerTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Lists_files_dirs_and_hidden_entries()
    {
        _tmp.Dir("src");
        _tmp.File("readme.md", "hello");
        _tmp.File(".gitignore", "bin/");

        var entries = DirectoryLister.List(_tmp.Path);

        Assert.Equal(3, entries.Count);
        var dir = Assert.Single(entries, e => e.IsDirectory);
        Assert.Equal("src", dir.Name);
        Assert.Equal("Folder", dir.TypeLabel);
        Assert.Equal(-1, dir.SizeBytes);

        var readme = Assert.Single(entries, e => e.Name == "readme.md");
        Assert.Equal(5, readme.SizeBytes);
        Assert.Equal("Markdown", readme.TypeLabel);
        Assert.Contains(entries, e => e.Name == ".gitignore");
    }

    [Fact]
    public void Unix_permissions_present_on_unix()
    {
        var file = _tmp.File("a.txt", "x");
        var entry = DirectoryLister.List(_tmp.Path).Single();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Matches("^[rwx-]{9}$", entry.UnixPermissions);
            Assert.Equal("RW", entry.AccessSummary);
        }

        _ = file;
    }
}

public class EntrySorterTests
{
    private static FileEntry Make(string name, bool dir, long size = 0, string type = "File") => new()
    {
        Name = name,
        FullPath = "/" + name,
        IsDirectory = dir,
        SizeBytes = dir ? -1 : size,
        TypeLabel = dir ? "Folder" : type,
        ModifiedUtc = DateTime.UnixEpoch.AddDays(size),
        UnixPermissions = "",
        AccessSummary = "RW",
    };

    [Fact]
    public void Directories_always_group_first()
    {
        var list = new[] { Make("zzz.txt", false), Make("aaa", true), Make("bbb.txt", false) };
        var byName = EntrySorter.Sort(list, SortColumn.Name, ascending: true);
        Assert.Equal(["aaa", "bbb.txt", "zzz.txt"], byName.Select(e => e.Name));

        var bySizeDesc = EntrySorter.Sort(list, SortColumn.Size, ascending: false);
        Assert.True(bySizeDesc[0].IsDirectory);
    }

    [Fact]
    public void Sorts_by_size_within_files()
    {
        var list = new[] { Make("small.txt", false, 10), Make("big.txt", false, 900), Make("dir", true) };
        var sorted = EntrySorter.Sort(list, SortColumn.Size, ascending: true);
        Assert.Equal(["dir", "small.txt", "big.txt"], sorted.Select(e => e.Name));
    }
}
