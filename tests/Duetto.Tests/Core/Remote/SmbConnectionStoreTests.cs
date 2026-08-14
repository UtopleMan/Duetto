using System.Security.Cryptography;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class SmbConnectionStoreTests
{
    private static readonly byte[] TestKey = SHA256.HashData("duetto-smb-test-v1"u8.ToArray());
    private static SecretCodec MakeCodec() => new(TestKey);

    private static SmbConnectionInfo MakeInfo(string id = "smb-1") => new(
        Id: id,
        Name: "NAS",
        Host: "nas.local",
        Port: 445,
        Username: "alice",
        Domain: "WORKGROUP",
        Guest: false,
        InitialPath: "/media");

    private static SmbConnectionStore MakeStore(out Dictionary<string, string> storage)
    {
        var files = new Dictionary<string, string>();
        storage = files;
        return new SmbConnectionStore(
            "smb-connections.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);
    }

    [Fact]
    public void Load_returns_empty_when_missing_empty_or_corrupt()
    {
        var store = MakeStore(out var files);
        Assert.Empty(store.Load());

        files["smb-connections.json"] = "";
        Assert.Empty(store.Load());

        files["smb-connections.json"] = "not json {{{";
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_then_Load_roundtrips_fields()
    {
        var store = MakeStore(out _);
        var stored = SmbConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("s3cret"), savePassword: true, MakeCodec());

        store.Save([stored]);
        var loaded = store.Load();

        Assert.Single(loaded);
        var only = loaded[0];
        Assert.Equal("smb-1", only.Id);
        Assert.Equal("NAS", only.Name);
        Assert.Equal("nas.local", only.Host);
        Assert.Equal(445, only.Port);
        Assert.Equal("alice", only.Username);
        Assert.Equal("WORKGROUP", only.Domain);
        Assert.False(only.Guest);
        Assert.Equal("/media", only.InitialPath);
        Assert.True(only.SavePassword);
    }

    [Fact]
    public void Pack_with_savePassword_obfuscates_and_Resolve_decrypts()
    {
        var codec = MakeCodec();
        var stored = SmbConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("hunter2"), savePassword: true, codec);

        Assert.NotEqual("hunter2", stored.ObfuscatedSecret);
        Assert.NotEmpty(stored.ObfuscatedSecret);

        var (info, secret) = SmbConnectionStore.Resolve(stored, codec);
        Assert.Equal("nas.local", info.Host);
        Assert.NotNull(secret);
        Assert.Equal("hunter2", secret.Password);
    }

    [Fact]
    public void Pack_without_savePassword_stores_no_secret_and_Resolve_returns_null()
    {
        var codec = MakeCodec();
        var stored = SmbConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("hunter2"), savePassword: false, codec);

        Assert.Empty(stored.ObfuscatedSecret);
        Assert.Null(SmbConnectionStore.ResolveSecret(stored, codec));
    }

    [Fact]
    public void Guest_connection_resolves_an_empty_secret_and_stores_none()
    {
        var codec = MakeCodec();
        var guest = MakeInfo() with { Guest = true, Username = "" };
        var stored = SmbConnectionStore.Pack(guest, ConnectSecret.FromPassword("ignored"), savePassword: true, codec);

        Assert.Empty(stored.ObfuscatedSecret);
        Assert.True(stored.Guest);

        var secret = SmbConnectionStore.ResolveSecret(stored, codec);
        Assert.NotNull(secret);
        Assert.Equal("", secret.Password);
    }
}
