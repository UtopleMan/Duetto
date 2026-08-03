using System.Text;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class S3FileStreamTests
{
    [Fact]
    public void ForWrite_spools_then_hands_rewound_stream_to_upload_on_close()
    {
        var payload = Encoding.UTF8.GetBytes("hello s3 world");
        byte[]? uploaded = null;

        using (var stream = S3FileStream.ForWrite(body =>
        {
            using var ms = new MemoryStream();
            body.CopyTo(ms);
            uploaded = ms.ToArray();
        }))
        {
            stream.Write(payload, 0, payload.Length);
            Assert.Null(uploaded); // upload happens only on close
        }

        Assert.NotNull(uploaded);
        Assert.Equal(payload, uploaded);
    }

    [Fact]
    public void ForWrite_uploads_multiple_writes_in_order()
    {
        byte[]? uploaded = null;
        using (var stream = S3FileStream.ForWrite(body =>
        {
            using var ms = new MemoryStream();
            body.CopyTo(ms);
            uploaded = ms.ToArray();
        }))
        {
            stream.Write([1, 2, 3], 0, 3);
            stream.Write([4, 5], 0, 2);
        }

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, uploaded);
    }

    [Fact]
    public void FakeAdapter_write_then_read_roundtrips_through_the_stream()
    {
        var adapter = new FakeS3ClientAdapter("duetto");
        var payload = Encoding.UTF8.GetBytes("object body bytes");

        using (var write = adapter.OpenWrite("duetto", "dir/file.txt"))
            write.Write(payload, 0, payload.Length);

        using var read = adapter.OpenRead("duetto", "dir/file.txt");
        using var ms = new MemoryStream();
        read.CopyTo(ms);

        Assert.Equal(payload, ms.ToArray());
        Assert.Equal(payload.Length, adapter.StatObject("duetto", "dir/file.txt")!.Length);
    }

    [Fact]
    public void FakeAdapter_lists_one_level_with_folders_and_files()
    {
        var adapter = new FakeS3ClientAdapter("duetto");
        adapter.Seed("duetto", "top.txt", [0]);
        adapter.Seed("duetto", "sub/a.txt", [0]);
        adapter.Seed("duetto", "sub/deep/b.txt", [0]);

        var root = adapter.ListObjects("duetto", "");
        Assert.Contains(root, e => e is { Name: "top.txt", IsDirectory: false });
        Assert.Contains(root, e => e is { Name: "sub", IsDirectory: true });
        Assert.DoesNotContain(root, e => e.Name == "a.txt");

        var sub = adapter.ListObjects("duetto", "sub/");
        Assert.Contains(sub, e => e is { Name: "a.txt", IsDirectory: false });
        Assert.Contains(sub, e => e is { Name: "deep", IsDirectory: true });
    }
}
