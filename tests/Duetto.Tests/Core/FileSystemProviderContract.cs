using System.Text;
using Duetto.Core.FileSystem;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

// Shared contract every IFileSystemProvider must satisfy: subclasses supply a provider and
// an empty Root so the same behaviour can be exercised against local, in-memory, and SFTP
// backends.
public abstract class FileSystemProviderContract
{
    protected abstract IFileSystemProvider Provider { get; }
    protected abstract string Root { get; }

    [Fact]
    public void CreateDirectory_is_listed_and_exists()
    {
        var dir = Provider.CreateDirectory(Root, "sub");
        Assert.True(Provider.DirectoryExists(dir));
        Assert.Contains(Provider.List(Root), e => e is { Name: "sub", IsDirectory: true });
    }

    [Fact]
    public void CreateFile_write_then_read_roundtrips()
    {
        var file = Provider.CreateFile(Root, "a.txt");
        var payload = Encoding.UTF8.GetBytes("hello sftp");
        using (var w = Provider.OpenWrite(file))
            w.Write(payload);

        using var r = Provider.OpenRead(file);
        using var ms = new MemoryStream();
        r.CopyTo(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public void Rename_moves_the_entry()
    {
        var file = Provider.CreateFile(Root, "old.txt");
        var renamed = Provider.Rename(file, "new.txt");
        Assert.False(Provider.FileExists(file));
        Assert.True(Provider.FileExists(renamed));
        Assert.Equal("new.txt", PathUtil.Leaf(renamed));
    }

    [Fact]
    public void ReplaceFile_overwrites_the_target_and_removes_the_source()
    {
        var target = Provider.CreateFile(Root, "final.txt");
        using (var w = Provider.OpenWrite(target))
            w.Write(Encoding.UTF8.GetBytes("stale"));

        var part = Provider.CreateFile(Root, "final.txt.part");
        using (var w = Provider.OpenWrite(part))
            w.Write(Encoding.UTF8.GetBytes("fresh"));

        Provider.ReplaceFile(part, target);

        Assert.False(Provider.FileExists(part));
        Assert.True(Provider.FileExists(target));
        using var r = Provider.OpenRead(target);
        using var ms = new MemoryStream();
        r.CopyTo(ms);
        Assert.Equal("fresh", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public void ReplaceFile_moves_onto_a_missing_target()
    {
        var part = Provider.CreateFile(Root, "new.txt.part");
        using (var w = Provider.OpenWrite(part))
            w.Write(Encoding.UTF8.GetBytes("payload"));

        var target = PathUtil.Combine(Root, "new.txt");
        Provider.ReplaceFile(part, target);

        Assert.False(Provider.FileExists(part));
        Assert.True(Provider.FileExists(target));
    }

    [Fact]
    public void Delete_permanent_removes_the_entry()
    {
        var file = Provider.CreateFile(Root, "doomed.txt");
        Provider.Delete(file, toTrash: false);
        Assert.False(Provider.FileExists(file));
    }

    [Fact]
    public void Stat_returns_null_for_a_missing_entry() =>
        Assert.Null(Provider.Stat(PathUtil.Combine(Root, "ghost")));

    [Fact]
    public void EnumerateRecursive_walks_the_subtree()
    {
        var sub = Provider.CreateDirectory(Root, "d");
        Provider.CreateFile(sub, "inner.txt");
        var names = Provider.EnumerateRecursive(Root).Select(e => e.Name).ToList();
        Assert.Contains("d", names);
        Assert.Contains("inner.txt", names);
    }

    [Fact]
    public void Move_file_cross_directory()
    {
        var src = Provider.CreateDirectory(Root, "src-dir");
        var dst = Provider.CreateDirectory(Root, "dst-dir");
        var file = Provider.CreateFile(src, "data.txt");
        var sep = Provider.Capabilities.Separator;
        var destPath = dst.TrimEnd(sep) + sep + "data.txt";
        Provider.Move(file, destPath);
        Assert.False(Provider.FileExists(file));
        Assert.True(Provider.FileExists(destPath));
    }

    [Fact]
    public void Move_directory_with_children()
    {
        var src = Provider.CreateDirectory(Root, "tree");
        Provider.CreateFile(src, "child.txt");
        var sep = Provider.Capabilities.Separator;
        var destPath = Root.TrimEnd(sep) + sep + "tree-moved";
        Provider.Move(src, destPath);
        Assert.False(Provider.DirectoryExists(src));
        Assert.True(Provider.DirectoryExists(destPath));
        Assert.True(Provider.FileExists(destPath.TrimEnd(sep) + sep + "child.txt"));
    }

    [Fact]
    public void Move_onto_existing_target_throws()
    {
        var src = Provider.CreateDirectory(Root, "src-exists");
        var file = Provider.CreateFile(src, "f.txt");
        var dst = Provider.CreateDirectory(Root, "dst-exists");
        var sep = Provider.Capabilities.Separator;
        var destFile = dst.TrimEnd(sep) + sep + "f.txt";
        Provider.CreateFile(dst, "f.txt");
        Assert.Throws<IOException>(() => Provider.Move(file, destFile));
        Assert.True(Provider.FileExists(file));
    }
}

public sealed class InMemoryFileSystemProviderContractTests : FileSystemProviderContract
{
    private readonly InMemoryFileSystemProvider _mem = new();
    protected override IFileSystemProvider Provider => _mem;
    protected override string Root => "/";
}

public sealed class LocalFileSystemProviderTests : FileSystemProviderContract, IDisposable
{
    private readonly TempDir _tmp = new();
    protected override IFileSystemProvider Provider { get; } = new LocalFileSystemProvider();
    protected override string Root => _tmp.Path;
    public void Dispose() => _tmp.Dispose();
}
