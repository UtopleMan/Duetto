using System.Text;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

// Real-server Azure Blob smoke tests, gated on the DUETTO_AZURE_TEST env var so they never run in
// CI. Bring Azurite up with docker-compose.yml (see scripts/smoke.sh). xunit 2.x has no Assert.Skip;
// tests return early when the gate is unset — an implicit 0-assertion pass.
[Trait("Category", "Integration")]
public sealed class AzureIntegrationTests
{
    // Azurite's well-known emulator account key (public, safe to embed in tests).
    private const string AzuriteKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private static bool TryConfig(out AzureConnectionInfo info, out ConnectSecret secret, out string container)
    {
        info = null!;
        secret = null!;
        container = null!;

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUETTO_AZURE_TEST")))
            return false;

        var endpoint = Environment.GetEnvironmentVariable("DUETTO_AZURE_TEST_ENDPOINT") ?? "http://127.0.0.1:10000/devstoreaccount1";
        var account = Environment.GetEnvironmentVariable("DUETTO_AZURE_TEST_ACCOUNT") ?? "devstoreaccount1";
        var key = Environment.GetEnvironmentVariable("DUETTO_AZURE_TEST_KEY") ?? AzuriteKey;
        container = Environment.GetEnvironmentVariable("DUETTO_AZURE_TEST_CONTAINER") ?? "duetto";

        info = new AzureConnectionInfo("it", "IT", Endpoint: endpoint, AccountName: account, AuthMode: AzureAuthMode.SharedKey);
        secret = ConnectSecret.FromPassword(key);

        // The container may not exist yet (Azurite starts empty). Create it with public blob access
        // so the anonymous test can read a seeded blob.
        var svc = new BlobServiceClient(new Uri(endpoint), new StorageSharedKeyCredential(account, key));
        svc.GetBlobContainerClient(container).CreateIfNotExists(PublicAccessType.Blob);
        return true;
    }

    private static AzureFileSystemProvider Connect(AzureConnectionInfo info, ConnectSecret secret)
    {
        var conn = new AzureConnection(info, secret);
        conn.Connect();
        return new AzureFileSystemProvider(conn);
    }

    [Fact]
    public void Lists_containers_at_root()
    {
        if (!TryConfig(out var info, out var secret, out var container))
            return;

        using var provider = Connect(info, secret);
        var containers = provider.List("/").Select(e => e.Name).ToList();
        Assert.Contains(container, containers);
    }

    [Fact]
    public void Full_lifecycle_roundtrip()
    {
        if (!TryConfig(out var info, out var secret, out var container))
            return;

        using var provider = Connect(info, secret);
        var root = "/" + container;
        var work = provider.CreateDirectory(root, "it-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Assert.True(provider.DirectoryExists(work));

            var file = provider.CreateFile(work, "hello.txt");
            var payload = Encoding.UTF8.GetBytes("hello azure integration");
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
        if (!TryConfig(out var info, out var secret, out var container))
            return;

        using var provider = Connect(info, secret);
        var root = "/" + container;
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
    public void Anonymous_reads_a_public_blob()
    {
        if (!TryConfig(out var info, out var secret, out var container))
            return;

        // Seed a blob with the authed connection.
        using var authed = Connect(info, secret);
        var key = "anon-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        var path = "/" + container + "/" + key;
        using (var w = authed.OpenWrite(path))
            w.Write(Encoding.UTF8.GetBytes("public payload"), 0, 14);

        try
        {
            // The container was created with public blob access. Anonymous cannot list, so it must
            // be scoped to the container and read the blob directly.
            var anonInfo = new AzureConnectionInfo("it-anon", "anon", Endpoint: info.Endpoint,
                AccountName: info.AccountName, AuthMode: AzureAuthMode.Anonymous, Container: container);
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
