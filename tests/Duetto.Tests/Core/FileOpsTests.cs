using Duetto.Core.Operations;

namespace Duetto.Tests.Core;

public class FileOpsTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Rename_moves_file_within_directory()
    {
        var src = _tmp.File("old.txt", "data");
        var renamed = FileOps.Rename(src, "new.txt");
        Assert.False(File.Exists(src));
        Assert.Equal("data", File.ReadAllText(renamed));
        Assert.Equal("new.txt", Path.GetFileName(renamed));
    }

    [Fact]
    public void Rename_rejects_path_separators()
    {
        var src = _tmp.File("a.txt");
        Assert.Throws<ArgumentException>(() => FileOps.Rename(src, "../evil.txt"));
    }

    [Fact]
    public void NewFolder_uniquifies_name()
    {
        var first = FileOps.NewFolder(_tmp.Path);
        var second = FileOps.NewFolder(_tmp.Path);
        Assert.Equal("New folder", Path.GetFileName(first));
        Assert.Equal("New folder 2", Path.GetFileName(second));
        Assert.True(Directory.Exists(first) && Directory.Exists(second));
    }
}

public class TrashServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Trash_removes_source()
    {
        var file = _tmp.File("doomed.txt", "bye");
        var trashed = TrashService.Trash(file);
        Assert.False(File.Exists(file));
        if (!OperatingSystem.IsWindows())
        {
            Assert.NotNull(trashed);
            Assert.True(File.Exists(trashed));
            File.Delete(trashed!);
        }
    }

    [Fact]
    public void Trash_missing_file_throws() =>
        Assert.Throws<FileNotFoundException>(() => TrashService.Trash(Path.Combine(_tmp.Path, "ghost")));
}
