using System.Security.Cryptography;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class S3ConnectionStoreTests
{
    private static readonly byte[] TestKey = SHA256.HashData("duetto-s3-test-v1"u8.ToArray());
    private static SecretCodec MakeCodec() => new(TestKey);

    private static S3ConnectionInfo MakeInfo(string id = "s3-1") => new(
        Id: id,
        Name: "MinIO",
        Endpoint: "http://127.0.0.1:9000",
        Region: "us-east-1",
        PathStyle: true,
        AuthMode: S3AuthMode.Keys,
        AccessKeyId: "AKIA123",
        Profile: "",
        Bucket: "",
        InitialPath: "/media");

    private static S3ConnectionStore MakeStore(out Dictionary<string, string> storage)
    {
        var files = new Dictionary<string, string>();
        storage = files;
        return new S3ConnectionStore(
            "s3-connections.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);
    }

    [Fact]
    public void Load_returns_empty_when_missing_empty_or_corrupt()
    {
        var store = MakeStore(out var files);
        Assert.Empty(store.Load());

        files["s3-connections.json"] = "";
        Assert.Empty(store.Load());

        files["s3-connections.json"] = "not json {{{";
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_then_Load_roundtrips_fields()
    {
        var store = MakeStore(out _);
        var stored = S3ConnectionStore.Pack(MakeInfo(), ConnectSecret.FromKeys("s3cret"), savePassword: true, MakeCodec());

        store.Save([stored]);
        var loaded = store.Load();

        Assert.Single(loaded);
        var only = loaded[0];
        Assert.Equal("s3-1", only.Id);
        Assert.Equal("MinIO", only.Name);
        Assert.Equal("http://127.0.0.1:9000", only.Endpoint);
        Assert.Equal("us-east-1", only.Region);
        Assert.True(only.PathStyle);
        Assert.Equal(S3AuthMode.Keys, only.AuthMode);
        Assert.Equal("AKIA123", only.AccessKeyId);
        Assert.Equal("/media", only.InitialPath);
        Assert.True(only.SavePassword);
    }

    [Fact]
    public void Pack_with_savePassword_obfuscates_secret_and_session_token_and_Resolve_decrypts()
    {
        var codec = MakeCodec();
        var stored = S3ConnectionStore.Pack(
            MakeInfo(),
            ConnectSecret.FromKeys("supersecret", "sts-token-xyz"),
            savePassword: true,
            codec);

        Assert.DoesNotContain("supersecret", stored.ObfuscatedSecret);
        Assert.DoesNotContain("sts-token-xyz", stored.ObfuscatedSecret);
        Assert.NotEmpty(stored.ObfuscatedSecret);

        var (info, secret) = S3ConnectionStore.Resolve(stored, codec);
        Assert.Equal("http://127.0.0.1:9000", info.Endpoint);
        Assert.NotNull(secret);
        Assert.Equal("supersecret", secret.Password);
        Assert.Equal("sts-token-xyz", secret.SessionToken);
    }

    [Fact]
    public void Pack_without_savePassword_stores_no_secret_and_Resolve_returns_null()
    {
        var codec = MakeCodec();
        var stored = S3ConnectionStore.Pack(MakeInfo(), ConnectSecret.FromKeys("supersecret"), savePassword: false, codec);

        Assert.Empty(stored.ObfuscatedSecret);
        Assert.Null(S3ConnectionStore.ResolveSecret(stored, codec));
    }

    [Fact]
    public void Profile_auth_stores_no_secret_and_resolves_an_empty_secret()
    {
        var codec = MakeCodec();
        var profile = MakeInfo() with { AuthMode = S3AuthMode.Profile, Profile = "work", AccessKeyId = "" };
        var stored = S3ConnectionStore.Pack(profile, ConnectSecret.FromKeys("ignored"), savePassword: true, codec);

        Assert.Empty(stored.ObfuscatedSecret);

        var secret = S3ConnectionStore.ResolveSecret(stored, codec);
        Assert.NotNull(secret);
        Assert.Null(secret.Password);
    }

    [Fact]
    public void Anonymous_auth_stores_no_secret_and_resolves_an_empty_secret()
    {
        var codec = MakeCodec();
        var anon = MakeInfo() with { AuthMode = S3AuthMode.Anonymous, Bucket = "duetto", AccessKeyId = "" };
        var stored = S3ConnectionStore.Pack(anon, ConnectSecret.FromKeys("ignored"), savePassword: true, codec);

        Assert.Empty(stored.ObfuscatedSecret);
        Assert.Equal("duetto", stored.Bucket);

        var secret = S3ConnectionStore.ResolveSecret(stored, codec);
        Assert.NotNull(secret);
    }
}
