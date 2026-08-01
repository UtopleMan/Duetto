using System.Text;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

// Real-server SMB smoke tests, gated on the DUETTO_SMB_TEST env var so they never run in CI.
// Bring the server up with docker-compose.smb.yml (see scripts/smb-it.sh). xunit 2.x has no
// Assert.Skip; tests return early when the gate is unset — an implicit 0-assertion pass.
[Trait("Category", "Integration")]
public sealed class SmbIntegrationTests
{
    private static bool TryConfig(out SmbConnectionInfo info, out ConnectSecret secret, out string share)
    {
        info = null!;
        secret = null!;
        share = null!;

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUETTO_SMB_TEST")))
            return false;

        var host = Environment.GetEnvironmentVariable("DUETTO_SMB_TEST_HOST") ?? "127.0.0.1";
        var user = Environment.GetEnvironmentVariable("DUETTO_SMB_TEST_USER") ?? "smbuser";
        var password = Environment.GetEnvironmentVariable("DUETTO_SMB_TEST_PASSWORD") ?? "smbpass";
        var domain = Environment.GetEnvironmentVariable("DUETTO_SMB_TEST_DOMAIN") ?? "WORKGROUP";
        share = Environment.GetEnvironmentVariable("DUETTO_SMB_TEST_SHARE") ?? "duetto";

        info = new SmbConnectionInfo("it", "IT", host, Username: user, Domain: domain);
        secret = ConnectSecret.FromPassword(password);
        return true;
    }

    private static SmbFileSystemProvider Connect(SmbConnectionInfo info, ConnectSecret secret)
    {
        var conn = new SmbConnection(info, secret);
        conn.Connect();
        return new SmbFileSystemProvider(conn);
    }

    [Fact]
    public void Lists_shares_at_root()
    {
        if (!TryConfig(out var info, out var secret, out var share))
            return;

        using var provider = Connect(info, secret);
        var shares = provider.List("/").Select(e => e.Name).ToList();
        Assert.Contains(share, shares);
    }

    [Fact]
    public void Full_lifecycle_roundtrip()
    {
        if (!TryConfig(out var info, out var secret, out var share))
            return;

        using var provider = Connect(info, secret);
        var root = "/" + share;
        var work = provider.CreateDirectory(root, "it-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            // create + write + read
            var file = provider.CreateFile(work, "hello.txt");
            var payload = Encoding.UTF8.GetBytes("hello smb integration");
            using (var w = provider.OpenWrite(file))
                w.Write(payload, 0, payload.Length);

            using (var r = provider.OpenRead(file))
            using (var ms = new MemoryStream())
            {
                r.CopyTo(ms);
                Assert.Equal(payload, ms.ToArray());
            }

            // stat + mtime
            var when = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            provider.SetLastWriteTimeUtc(file, when);
            var stat = provider.Stat(file);
            Assert.NotNull(stat);
            Assert.Equal(when, stat.ModifiedUtc);
            Assert.Equal(payload.Length, stat.SizeBytes);

            // rename
            var renamed = provider.Rename(file, "renamed.txt");
            Assert.True(provider.FileExists(renamed));
            Assert.False(provider.FileExists(file));

            // atomic replace (".part" finish)
            var part = provider.CreateFile(work, "renamed.txt.part");
            using (var w = provider.OpenWrite(part))
                w.Write(Encoding.UTF8.GetBytes("fresh"), 0, 5);
            provider.ReplaceFile(part, renamed);
            Assert.False(provider.FileExists(part));
            using (var r = provider.OpenRead(renamed))
            using (var ms = new MemoryStream())
            {
                r.CopyTo(ms);
                Assert.Equal("fresh", Encoding.UTF8.GetString(ms.ToArray()));
            }

            // recursive enumerate
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
    public void Guest_reads_public_share()
    {
        if (!TryConfig(out var info, out var secret, out _))
            return;

        var guestInfo = info with { Username = "", Guest = true };
        using var provider = Connect(guestInfo, ConnectSecret.FromPassword(""));

        var publicShare = Environment.GetEnvironmentVariable("DUETTO_SMB_TEST_GUEST_SHARE") ?? "public";
        var root = "/" + publicShare;

        var probe = provider.CreateFile(root, "guest-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        try
        {
            using (var w = provider.OpenWrite(probe))
                w.Write(Encoding.UTF8.GetBytes("guest"), 0, 5);
            Assert.True(provider.FileExists(probe));
        }
        finally
        {
            provider.Delete(probe, toTrash: false);
        }
    }
}
