using Duetto.Core.Remote;
using System.Security.Cryptography;
using System.Text.Json;

namespace Duetto.Tests.Core.Remote;

public class ConnectionStoreTests
{
    private static readonly byte[] TestKey = SHA256.HashData("duetto-cs-test-v1"u8.ToArray());
    private static SecretCodec MakeCodec() => new(TestKey);

    private static ConnectionInfo MakeInfo(string id = "conn-1") => new(
        Id: id,
        Name: "My Server",
        Host: "example.com",
        Port: 22,
        Username: "alice",
        AuthMode: AuthMode.Password,
        KeyPath: null,
        InitialRemotePath: "/home/alice");

    private static ConnectionInfo MakeKeyInfo(string id = "conn-2") => new(
        Id: id,
        Name: "Key Server",
        Host: "key.example.com",
        Port: 2222,
        Username: "bob",
        AuthMode: AuthMode.Key,
        KeyPath: "/home/bob/.ssh/id_ed25519",
        InitialRemotePath: "/");

    private static ConnectionStore MakeStore(out Dictionary<string, string> storage)
    {
        var files = new Dictionary<string, string>();
        storage = files;
        return new ConnectionStore(
            "connections.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);
    }

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var store = MakeStore(out _);
        var result = store.Load();
        Assert.Empty(result);
    }

    [Fact]
    public void Load_returns_empty_for_empty_content()
    {
        var files = new Dictionary<string, string> { ["connections.json"] = "" };
        var store = new ConnectionStore(
            "connections.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_returns_empty_for_corrupt_json()
    {
        var files = new Dictionary<string, string> { ["connections.json"] = "not json at all {{{{" };
        var store = new ConnectionStore(
            "connections.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_accepts_hand_edited_property_name_casing()
    {
        var json = """
            [
              {
                "Id": "conn-1",
                "Name": "My Server",
                "Host": "example.com",
                "Port": 2200,
                "Username": "alice",
                "AuthMode": "Key",
                "KeyPath": "/home/alice/.ssh/id_ed25519",
                "InitialRemotePath": "/home/alice",
                "SavePassword": true,
                "ObfuscatedSecret": "abc"
              }
            ]
            """;
        var files = new Dictionary<string, string> { ["connections.json"] = json };
        var store = new ConnectionStore(
            "connections.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);

        var loaded = store.Load();

        Assert.Single(loaded);
        var sc = loaded[0];
        Assert.Equal("conn-1", sc.Id);
        Assert.Equal("My Server", sc.Name);
        Assert.Equal("example.com", sc.Host);
        Assert.Equal(2200, sc.Port);
        Assert.Equal("alice", sc.Username);
        Assert.Equal(AuthMode.Key, sc.AuthMode);
        Assert.Equal("/home/alice/.ssh/id_ed25519", sc.KeyPath);
        Assert.Equal("/home/alice", sc.InitialRemotePath);
        Assert.True(sc.SavePassword);
        Assert.Equal("abc", sc.ObfuscatedSecret);
    }

    [Fact]
    public void Save_and_Load_round_trips_empty_array()
    {
        var store = MakeStore(out _);
        store.Save([]);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_and_Load_round_trips_connection_without_password()
    {
        var store = MakeStore(out _);
        var info = MakeInfo();
        var packed = ConnectionStore.Pack(info, secret: null, savePassword: false, MakeCodec());

        store.Save([packed]);
        var loaded = store.Load();

        Assert.Single(loaded);
        var sc = loaded[0];
        Assert.Equal("conn-1", sc.Id);
        Assert.Equal("My Server", sc.Name);
        Assert.Equal("example.com", sc.Host);
        Assert.Equal(22, sc.Port);
        Assert.Equal("alice", sc.Username);
        Assert.Equal(AuthMode.Password, sc.AuthMode);
        Assert.Null(sc.KeyPath);
        Assert.Equal("/home/alice", sc.InitialRemotePath);
        Assert.False(sc.SavePassword);
        Assert.Equal(string.Empty, sc.ObfuscatedSecret);
    }

    [Fact]
    public void Save_and_Load_round_trips_connection_with_saved_password()
    {
        var codec = MakeCodec();
        var store = MakeStore(out _);
        var info = MakeInfo();
        var secret = ConnectSecret.FromPassword("hunter2");
        var packed = ConnectionStore.Pack(info, secret, savePassword: true, codec);

        store.Save([packed]);
        var loaded = store.Load();

        Assert.Single(loaded);
        var resolved = ConnectionStore.ResolveSecret(loaded[0], codec);
        Assert.NotNull(resolved);
        Assert.Equal("hunter2", resolved!.Password);
    }

    [Fact]
    public void Save_and_Load_round_trips_key_connection_with_passphrase()
    {
        var codec = MakeCodec();
        var store = MakeStore(out _);
        var info = MakeKeyInfo();
        var secret = ConnectSecret.FromKey("mypassphrase");
        var packed = ConnectionStore.Pack(info, secret, savePassword: true, codec);

        store.Save([packed]);
        var loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal(AuthMode.Key, loaded[0].AuthMode);
        Assert.Equal("/home/bob/.ssh/id_ed25519", loaded[0].KeyPath);

        var resolved = ConnectionStore.ResolveSecret(loaded[0], codec);
        Assert.NotNull(resolved);
        Assert.Equal("mypassphrase", resolved!.KeyPassphrase);
    }

    [Fact]
    public void KeyPath_is_always_saved_regardless_of_SavePassword_flag()
    {
        var store = MakeStore(out _);
        var info = MakeKeyInfo();
        var packed = ConnectionStore.Pack(info, secret: null, savePassword: false, MakeCodec());

        store.Save([packed]);
        var loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal("/home/bob/.ssh/id_ed25519", loaded[0].KeyPath);
        Assert.False(loaded[0].SavePassword);
        Assert.Equal(string.Empty, loaded[0].ObfuscatedSecret);
    }

    [Fact]
    public void Multiple_connections_round_trip()
    {
        var codec = MakeCodec();
        var store = MakeStore(out _);

        var packed = new[]
        {
            ConnectionStore.Pack(MakeInfo("a"), ConnectSecret.FromPassword("pw1"), true, codec),
            ConnectionStore.Pack(MakeKeyInfo("b"), ConnectSecret.FromKey("pp2"), true, codec),
        };

        store.Save(packed);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Length);
        Assert.Equal("a", loaded[0].Id);
        Assert.Equal("b", loaded[1].Id);
    }

    [Fact]
    public void ResolveInfo_produces_correct_ConnectionInfo()
    {
        var info = MakeInfo();
        var packed = ConnectionStore.Pack(info, null, false, MakeCodec());
        var resolved = ConnectionStore.ResolveInfo(packed);

        Assert.Equal(info, resolved);
    }

    [Fact]
    public void ResolveSecret_returns_null_when_SavePassword_false()
    {
        var codec = MakeCodec();
        var packed = ConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("pw"), savePassword: false, codec);
        Assert.Null(ConnectionStore.ResolveSecret(packed, codec));
    }

    [Fact]
    public void ResolveSecret_returns_null_for_corrupt_ciphertext()
    {
        var stored = new StoredConnection
        {
            Id = "x",
            AuthMode = AuthMode.Password,
            SavePassword = true,
            ObfuscatedSecret = "notvalidbase64!!!",
        };
        Assert.Null(ConnectionStore.ResolveSecret(stored, MakeCodec()));
    }

    [Fact]
    public void ResolveSecret_returns_null_for_foreign_machine_ciphertext()
    {
        var otherCodec = new SecretCodec(SHA256.HashData("other-machine"u8.ToArray()));
        var myCodec = MakeCodec();

        var cipher = otherCodec.Encrypt("secret");
        var stored = new StoredConnection
        {
            Id = "y",
            AuthMode = AuthMode.Password,
            SavePassword = true,
            ObfuscatedSecret = cipher,
        };

        Assert.Null(ConnectionStore.ResolveSecret(stored, myCodec));
    }

    [Fact]
    public void Resolve_convenience_returns_info_and_secret_together()
    {
        var codec = MakeCodec();
        var info = MakeInfo();
        var secret = ConnectSecret.FromPassword("pw");
        var packed = ConnectionStore.Pack(info, secret, savePassword: true, codec);

        var (resolvedInfo, resolvedSecret) = ConnectionStore.Resolve(packed, codec);

        Assert.Equal(info, resolvedInfo);
        Assert.NotNull(resolvedSecret);
        Assert.Equal("pw", resolvedSecret!.Password);
    }

    [Fact]
    public void Resolve_without_saved_password_returns_null_secret()
    {
        var codec = MakeCodec();
        var packed = ConnectionStore.Pack(MakeInfo(), null, savePassword: false, codec);
        var (_, resolvedSecret) = ConnectionStore.Resolve(packed, codec);
        Assert.Null(resolvedSecret);
    }

    [Fact]
    public void Pack_with_savePassword_sets_ObfuscatedSecret_non_empty()
    {
        var codec = MakeCodec();
        var packed = ConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("pw"), savePassword: true, codec);
        Assert.True(packed.SavePassword);
        Assert.NotEmpty(packed.ObfuscatedSecret);
    }

    [Fact]
    public void Pack_without_savePassword_has_empty_ObfuscatedSecret()
    {
        var codec = MakeCodec();
        var packed = ConnectionStore.Pack(MakeInfo(), ConnectSecret.FromPassword("pw"), savePassword: false, codec);
        Assert.False(packed.SavePassword);
        Assert.Equal(string.Empty, packed.ObfuscatedSecret);
    }

    [Fact]
    public void Pack_key_connection_with_null_passphrase_stores_empty_secret()
    {
        var codec = MakeCodec();
        var info = MakeKeyInfo();
        var packed = ConnectionStore.Pack(info, ConnectSecret.FromKey(null), savePassword: true, codec);
        Assert.True(packed.SavePassword);
        var resolved = ConnectionStore.ResolveSecret(packed, codec);
        Assert.NotNull(resolved);
        Assert.Null(resolved!.KeyPassphrase);
    }
}
