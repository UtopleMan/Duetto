using System.Text;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class AzureFileSystemProviderContractTests
{
    private static (AzureFileSystemProvider Provider, FakeAzureClientAdapter Adapter) Make(string configuredContainer = "")
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        var info = new AzureConnectionInfo(Id: "c1", Name: "n", Container: configuredContainer);
        var conn = new AzureConnection(info, new ConnectSecret(), new FakeAzureClientFactory(adapter));
        conn.Connect();
        return (new AzureFileSystemProvider(conn), adapter);
    }

    [Fact]
    public void Capabilities_reflect_object_store_semantics()
    {
        var caps = AzureFileSystemProvider.AzureCapabilities;
        Assert.False(caps.CanRename);
        Assert.False(caps.HasTrash);
        Assert.False(caps.AtomicRename);
        Assert.False(caps.PreservesMTime);
        Assert.True(caps.CanCreateEmptyDir);
        Assert.True(caps.SupportsSearch);
        Assert.Equal('/', caps.Separator);
    }

    [Fact]
    public void List_root_returns_containers()
    {
        var (provider, _) = Make();
        var root = provider.List("/");
        Assert.Single(root);
        Assert.Equal("duetto", root[0].Name);
        Assert.True(root[0].IsDirectory);
    }

    [Fact]
    public void List_root_in_single_container_mode_lists_only_that_container()
    {
        var (provider, _) = Make(configuredContainer: "duetto");
        var root = provider.List("/");
        Assert.Single(root);
        Assert.Equal("duetto", root[0].Name);
    }

    [Fact]
    public void List_folder_returns_files_and_subfolders()
    {
        var (provider, adapter) = Make();
        adapter.Seed("duetto", "top.txt", [1]);
        adapter.Seed("duetto", "sub/a.txt", [1]);

        var list = provider.List("/duetto");
        Assert.Contains(list, e => e is { Name: "top.txt", IsDirectory: false });
        Assert.Contains(list, e => e is { Name: "sub", IsDirectory: true });
    }

    [Fact]
    public void CreateDirectory_writes_a_marker_and_DirectoryExists_sees_it()
    {
        var (provider, _) = Make();
        var path = provider.CreateDirectory("/duetto", "newdir");
        Assert.Equal("/duetto/newdir", path);
        Assert.True(provider.DirectoryExists("/duetto/newdir"));
    }

    [Fact]
    public void CreateFile_then_FileExists_and_Stat()
    {
        var (provider, _) = Make();
        var path = provider.CreateFile("/duetto", "f.txt");
        Assert.Equal("/duetto/f.txt", path);
        Assert.True(provider.FileExists("/duetto/f.txt"));
        Assert.Equal(0, provider.Stat("/duetto/f.txt")!.SizeBytes);
    }

    [Fact]
    public void OpenWrite_then_OpenRead_roundtrips_bytes()
    {
        var (provider, _) = Make();
        var payload = Encoding.UTF8.GetBytes("azure provider bytes");

        using (var w = provider.OpenWrite("/duetto/dir/a.txt"))
            w.Write(payload, 0, payload.Length);

        using var r = provider.OpenRead("/duetto/dir/a.txt");
        using var ms = new MemoryStream();
        r.CopyTo(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public void Delete_file_removes_only_that_blob()
    {
        var (provider, adapter) = Make();
        adapter.Seed("duetto", "keep.txt", [1]);
        adapter.Seed("duetto", "drop.txt", [1]);

        provider.Delete("/duetto/drop.txt", toTrash: false);

        Assert.False(provider.FileExists("/duetto/drop.txt"));
        Assert.True(provider.FileExists("/duetto/keep.txt"));
    }

    [Fact]
    public void Delete_folder_removes_the_whole_prefix()
    {
        var (provider, adapter) = Make();
        adapter.Seed("duetto", "d/1.txt", [1]);
        adapter.Seed("duetto", "d/2.txt", [1]);
        adapter.Seed("duetto", "e/3.txt", [1]);

        provider.Delete("/duetto/d", toTrash: false);

        Assert.False(provider.DirectoryExists("/duetto/d"));
        Assert.True(provider.FileExists("/duetto/e/3.txt"));
    }

    [Fact]
    public void EnumerateRecursive_yields_folders_and_files()
    {
        var (provider, adapter) = Make();
        adapter.Seed("duetto", "top.txt", [1]);
        adapter.Seed("duetto", "sub/a.txt", [1]);
        adapter.Seed("duetto", "sub/deep/b.txt", [1]);

        var all = provider.EnumerateRecursive("/duetto").ToList();
        Assert.Contains(all, e => e is { Name: "sub", IsDirectory: true });
        Assert.Contains(all, e => e is { Name: "deep", IsDirectory: true });
        Assert.Contains(all, e => e is { Name: "b.txt", IsDirectory: false });
    }

    [Fact]
    public void Rename_file_copies_to_new_key_and_deletes_old()
    {
        var (provider, adapter) = Make();
        adapter.Seed("duetto", "old.txt", [1, 2, 3]);

        var path = provider.Rename("/duetto/old.txt", "new.txt");

        Assert.Equal("/duetto/new.txt", path);
        Assert.True(provider.FileExists("/duetto/new.txt"));
        Assert.False(provider.FileExists("/duetto/old.txt"));
    }

    [Fact]
    public void Move_file_copies_then_deletes_source()
    {
        var (provider, adapter) = Make();
        adapter.Seed("duetto", "m.txt", [1, 2]);

        provider.Move("/duetto/m.txt", "/duetto/moved/m.txt");

        Assert.True(provider.FileExists("/duetto/moved/m.txt"));
        Assert.False(provider.FileExists("/duetto/m.txt"));
    }

    [Fact]
    public void SetLastWriteTimeUtc_is_not_supported()
    {
        var (provider, _) = Make();
        Assert.Throws<NotSupportedException>(() => provider.SetLastWriteTimeUtc("/duetto/x.txt", DateTime.UtcNow));
    }
}
