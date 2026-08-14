using System.Text;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

[Trait("Category", "Integration")]
public sealed class S3IntegrationTests
{
    private static bool TryConfig(out S3ConnectionInfo info, out ConnectSecret secret, out string bucket)
    {
        info = null!;
        secret = null!;
        bucket = null!;

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUETTO_S3_TEST")))
            return false;

        var endpoint = Environment.GetEnvironmentVariable("DUETTO_S3_TEST_ENDPOINT") ?? "http://127.0.0.1:9000";
        var access = Environment.GetEnvironmentVariable("DUETTO_S3_TEST_ACCESS") ?? "duetto";
        var s3secret = Environment.GetEnvironmentVariable("DUETTO_S3_TEST_SECRET") ?? "duettosecret";
        bucket = Environment.GetEnvironmentVariable("DUETTO_S3_TEST_BUCKET") ?? "duetto";

        info = new S3ConnectionInfo("it", "IT", Endpoint: endpoint, Region: "us-east-1",
            PathStyle: true, AuthMode: S3AuthMode.Keys, AccessKeyId: access);
        secret = ConnectSecret.FromKeys(s3secret);
        return true;
    }

    private static S3FileSystemProvider Connect(S3ConnectionInfo info, ConnectSecret secret)
    {
        var conn = new S3Connection(info, secret);
        conn.Connect();
        return new S3FileSystemProvider(conn);
    }

    [Fact]
    public void Lists_buckets_at_root()
    {
        if (!TryConfig(out var info, out var secret, out var bucket))
            return;

        using var provider = Connect(info, secret);
        var buckets = provider.List("/").Select(e => e.Name).ToList();
        Assert.Contains(bucket, buckets);
    }

    [Fact]
    public void Full_lifecycle_roundtrip()
    {
        if (!TryConfig(out var info, out var secret, out var bucket))
            return;

        using var provider = Connect(info, secret);
        var root = "/" + bucket;
        var work = provider.CreateDirectory(root, "it-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Assert.True(provider.DirectoryExists(work));

            var file = provider.CreateFile(work, "hello.txt");
            var payload = Encoding.UTF8.GetBytes("hello s3 integration");
            using (var w = provider.OpenWrite(file))
                w.Write(payload, 0, payload.Length);

            using (var r = provider.OpenRead(file))
            using (var ms = new MemoryStream())
            {
                r.CopyTo(ms);
                Assert.Equal(payload, ms.ToArray());
            }

            var stat = provider.Stat(file);
            Assert.NotNull(stat);
            Assert.Equal(payload.Length, stat.SizeBytes);

            var renamed = provider.Rename(file, "renamed.txt");
            Assert.True(provider.FileExists(renamed));
            Assert.False(provider.FileExists(file));

            var names = provider.EnumerateRecursive(work).Select(e => e.Name).ToList();
            Assert.Contains("renamed.txt", names);
        }
        finally
        {
            provider.Delete(work, toTrash: false);
            Assert.False(provider.DirectoryExists(work));
        }
    }

    [Fact]
    public void Server_side_copy_copies_bytes_exactly()
    {
        if (!TryConfig(out var info, out var secret, out var bucket))
            return;

        using var provider = Connect(info, secret);
        var root = "/" + bucket;
        var work = provider.CreateDirectory(root, "cc-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var payload = new byte[(2 * 1024 * 1024) + 777];
            new Random(1234).NextBytes(payload);
            var srcFile = provider.CreateFile(work, "src.bin");
            using (var w = provider.OpenWrite(srcFile))
                w.Write(payload, 0, payload.Length);

            var dstFile = work + "/dst.bin";
            long reported = 0;
            var ok = ((IServerSideCopy)provider).TryServerSideCopy(
                srcFile, dstFile, n => reported += n, CancellationToken.None);
            Assert.True(ok);
            Assert.Equal(payload.Length, reported);

            using var r = provider.OpenRead(dstFile);
            using var ms = new MemoryStream();
            r.CopyTo(ms);
            Assert.Equal(payload, ms.ToArray());
        }
        finally
        {
            provider.Delete(work, toTrash: false);
        }
    }

    [Fact]
    public void Anonymous_reads_a_public_object()
    {
        if (!TryConfig(out var info, out var secret, out var bucket))
            return;

        using var authed = Connect(info, secret);
        var key = "anon-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        var path = "/" + bucket + "/" + key;
        using (var w = authed.OpenWrite(path))
            w.Write(Encoding.UTF8.GetBytes("public payload"), 0, 14);

        try
        {
            var anonInfo = new S3ConnectionInfo("it-anon", "anon", Endpoint: info.Endpoint,
                Region: info.Region, PathStyle: true, AuthMode: S3AuthMode.Anonymous, Bucket: bucket);
            using var anon = Connect(anonInfo, new ConnectSecret());

            using var r = anon.OpenRead(path);
            using var ms = new MemoryStream();
            r.CopyTo(ms);
            Assert.Equal("public payload", Encoding.UTF8.GetString(ms.ToArray()));
        }
        finally
        {
            authed.Delete(path, toTrash: false);
        }
    }
}
