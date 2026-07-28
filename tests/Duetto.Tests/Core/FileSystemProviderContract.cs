using System.Text;
using Duetto.Core.FileSystem;

namespace Duetto.Tests.Core;

/// <summary>
/// Behavior every <see cref="IFileSystemProvider"/> must satisfy. Concrete subclasses
/// supply a provider and an existing empty <see cref="Root"/>; the local provider runs
/// it here, the SFTP provider reuses it against a fake backend in Phase 2.
/// </summary>
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
}

public sealed class LocalFileSystemProviderTests : FileSystemProviderContract, IDisposable
{
    private readonly TempDir _tmp = new();
    protected override IFileSystemProvider Provider { get; } = new LocalFileSystemProvider();
    protected override string Root => _tmp.Path;
    public void Dispose() => _tmp.Dispose();
}
