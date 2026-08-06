using System.Security.Cryptography;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class AzureConnectionStoreTests
{
    private static readonly byte[] TestKey = SHA256.HashData("duetto-azure-test-v1"u8.ToArray());
    private static SecretCodec MakeCodec() => new(TestKey);

    private static AzureConnectionInfo MakeInfo(string id = "az-1") => new(
        Id: id,
        Name: "Azurite",
        Endpoint: "http://127.0.0.1:10000/devstoreaccount1",
        AccountName: "devstoreaccount1",
        AuthMode: AzureAuthMode.SharedKey,
        Container: "",
        InitialPath: "/media");

    private static AzureConnectionStore MakeStore(out Dictionary<string, string> storage)
    {
        var files = new Dictionary<string, string>();
        storage = files;
        return new AzureConnectionStore(
            "azure-connections.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);
    }

    [Fact]
    public void Load_returns_empty_when_missing_empty_or_corrupt()
    {
        var store = MakeStore(out var files);
        Assert.Empty(store.Load());

        files["azure-connections.json"] = "";
        Assert.Empty(store.Load());

        files["azure-connections.json"] = "not json {{{";
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_then_Load_roundtrips_fields()
    {
        var store = MakeStore(out _);
        var stored = AzureConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("accountkey=="), savePassword: true, MakeCodec());

        store.Save([stored]);
        var loaded = store.Load();

        Assert.Single(loaded);
        var only = loaded[0];
        Assert.Equal("az-1", only.Id);
        Assert.Equal("Azurite", only.Name);
        Assert.Equal("http://127.0.0.1:10000/devstoreaccount1", only.Endpoint);
        Assert.Equal("devstoreaccount1", only.AccountName);
        Assert.Equal(AzureAuthMode.SharedKey, only.AuthMode);
        Assert.Equal("/media", only.InitialPath);
        Assert.True(only.SavePassword);
    }

    [Theory]
    [InlineData(AzureAuthMode.SharedKey)]
    [InlineData(AzureAuthMode.ConnectionString)]
    [InlineData(AzureAuthMode.Sas)]
    public void Pack_with_savePassword_obfuscates_secret_and_Resolve_decrypts(AzureAuthMode mode)
    {
        var codec = MakeCodec();
        var info = MakeInfo() with { AuthMode = mode };
        var stored = AzureConnectionStore.Pack(
            info,
            ConnectSecret.FromPassword("supersecret-value"),
            savePassword: true,
            codec);

        Assert.DoesNotContain("supersecret-value", stored.ObfuscatedSecret);
        Assert.NotEmpty(stored.ObfuscatedSecret);

        var (resolvedInfo, secret) = AzureConnectionStore.Resolve(stored, codec);
        Assert.Equal("http://127.0.0.1:10000/devstoreaccount1", resolvedInfo.Endpoint);
        Assert.NotNull(secret);
        Assert.Equal("supersecret-value", secret.Password);
    }

    [Fact]
    public void Pack_without_savePassword_stores_no_secret_and_Resolve_returns_null()
    {
        var codec = MakeCodec();
        var stored = AzureConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("supersecret"), savePassword: false, codec);

        Assert.Empty(stored.ObfuscatedSecret);
        Assert.Null(AzureConnectionStore.ResolveSecret(stored, codec));
    }

    [Fact]
    public void Anonymous_auth_stores_no_secret_and_resolves_an_empty_secret()
    {
        var codec = MakeCodec();
        var anon = MakeInfo() with { AuthMode = AzureAuthMode.Anonymous, Container = "duetto", AccountName = "" };
        var stored = AzureConnectionStore.Pack(anon, ConnectSecret.FromPassword("ignored"), savePassword: true, codec);

        Assert.Empty(stored.ObfuscatedSecret);
        Assert.Equal("duetto", stored.Container);

        var secret = AzureConnectionStore.ResolveSecret(stored, codec);
        Assert.NotNull(secret);
        Assert.Null(secret.Password);
    }
}
