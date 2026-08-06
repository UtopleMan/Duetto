using System.Text;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class AzureFileStreamTests
{
    [Fact]
    public void FakeAdapter_write_then_read_roundtrips_through_the_stream()
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        var payload = Encoding.UTF8.GetBytes("blob body bytes");

        using (var write = adapter.OpenWrite("duetto", "dir/file.txt"))
            write.Write(payload, 0, payload.Length);

        using var read = adapter.OpenRead("duetto", "dir/file.txt");
        using var ms = new MemoryStream();
        read.CopyTo(ms);

        Assert.Equal(payload, ms.ToArray());
        Assert.Equal(payload.Length, adapter.StatBlob("duetto", "dir/file.txt")!.Length);
    }

    [Fact]
    public void FakeAdapter_lists_one_level_with_folders_and_files()
    {
        var adapter = new FakeAzureClientAdapter("duetto");
        adapter.Seed("duetto", "top.txt", [0]);
        adapter.Seed("duetto", "sub/a.txt", [0]);
        adapter.Seed("duetto", "sub/deep/b.txt", [0]);

        var root = adapter.ListBlobs("duetto", "");
        Assert.Contains(root, e => e is { Name: "top.txt", IsDirectory: false });
        Assert.Contains(root, e => e is { Name: "sub", IsDirectory: true });
        Assert.DoesNotContain(root, e => e.Name == "a.txt");

        var sub = adapter.ListBlobs("duetto", "sub/");
        Assert.Contains(sub, e => e is { Name: "a.txt", IsDirectory: false });
        Assert.Contains(sub, e => e is { Name: "deep", IsDirectory: true });
    }
}
