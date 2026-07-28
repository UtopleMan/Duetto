using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Tests.Support;

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

    [Fact]
    public void SuggestEntryName_returns_free_name_without_creating()
    {
        var name = FileOps.SuggestEntryName(_tmp.Path, "New folder");
        Assert.Equal("New folder", name);
        Assert.False(Directory.Exists(Path.Combine(_tmp.Path, name)));
        Assert.False(File.Exists(Path.Combine(_tmp.Path, name)));
    }

    [Fact]
    public void SuggestEntryName_uniquifies_around_existing_entries()
    {
        _tmp.Dir("New folder");
        _tmp.File("New folder 2");
        Assert.Equal("New folder 3", FileOps.SuggestEntryName(_tmp.Path, "New folder"));
    }

    [Fact]
    public void CreateFolder_creates_exact_name()
    {
        var created = FileOps.CreateFolder(_tmp.Path, "Photos");
        Assert.Equal("Photos", Path.GetFileName(created));
        Assert.True(Directory.Exists(created));
    }

    [Fact]
    public void CreateFolder_throws_when_target_exists()
    {
        _tmp.Dir("Photos");
        Assert.Throws<IOException>(() => FileOps.CreateFolder(_tmp.Path, "Photos"));
    }

    [Fact]
    public void CreateFolder_rejects_path_separators() =>
        Assert.Throws<ArgumentException>(() => FileOps.CreateFolder(_tmp.Path, "a/b"));

    [Fact]
    public void CreateFile_creates_empty_file()
    {
        var created = FileOps.CreateFile(_tmp.Path, "notes.txt");
        Assert.Equal("notes.txt", Path.GetFileName(created));
        Assert.True(File.Exists(created));
        Assert.Equal(0, new FileInfo(created).Length);
    }

    [Fact]
    public void CreateFile_throws_when_target_exists()
    {
        _tmp.File("notes.txt", "keep me");
        Assert.Throws<IOException>(() => FileOps.CreateFile(_tmp.Path, "notes.txt"));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_tmp.Path, "notes.txt")));
    }

    [Fact]
    public void CreateFile_rejects_path_separators() =>
        Assert.Throws<ArgumentException>(() => FileOps.CreateFile(_tmp.Path, "a/b.txt"));
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

/// <summary>
/// Pins the provider-aware <see cref="FileOps"/> overloads: they route entirely through the
/// <see cref="IFileSystemProvider"/> seam (proven against the '/'-rooted in-memory fake, which
/// never touches local disk) while keeping the shared name validation and clobber guards.
/// </summary>
public class FileOpsProviderTests
{
    private readonly InMemoryFileSystemProvider _fs = new();

    [Fact]
    public void CreateFolder_routes_through_the_provider()
    {
        var created = FileOps.CreateFolder(_fs, "/", "Photos");
        Assert.Equal("/Photos", created);
        Assert.True(_fs.DirectoryExists(created));
    }

    [Fact]
    public void CreateFolder_throws_when_target_exists()
    {
        FileOps.CreateFolder(_fs, "/", "Photos");
        Assert.Throws<IOException>(() => FileOps.CreateFolder(_fs, "/", "Photos"));
    }

    [Fact]
    public void CreateFile_throws_when_target_exists()
    {
        FileOps.CreateFile(_fs, "/", "notes.txt");
        Assert.Throws<IOException>(() => FileOps.CreateFile(_fs, "/", "notes.txt"));
    }

    [Fact]
    public void CreateFile_rejects_path_separators() =>
        Assert.Throws<ArgumentException>(() => FileOps.CreateFile(_fs, "/", "a/b.txt"));

    [Fact]
    public void SuggestEntryName_uniquifies_against_provider_entries()
    {
        _fs.CreateDirectory("/", "New folder");
        _fs.CreateFile("/", "New folder 2");
        Assert.Equal("New folder 3", FileOps.SuggestEntryName(_fs, "/", "New folder"));
    }

    [Fact]
    public void Rename_routes_through_the_provider()
    {
        var file = FileOps.CreateFile(_fs, "/", "old.txt");
        var renamed = FileOps.Rename(_fs, file, "new.txt");
        Assert.False(_fs.FileExists(file));
        Assert.True(_fs.FileExists(renamed));
        Assert.Equal("new.txt", PathUtil.Leaf(renamed));
    }

    [Fact]
    public void Rename_rejects_path_separators()
    {
        var file = FileOps.CreateFile(_fs, "/", "a.txt");
        Assert.Throws<ArgumentException>(() => FileOps.Rename(_fs, file, "../evil.txt"));
    }

    [Fact]
    public void Rename_rejects_a_root() =>
        Assert.Throws<ArgumentException>(() => FileOps.Rename(_fs, "/", "x"));
}
