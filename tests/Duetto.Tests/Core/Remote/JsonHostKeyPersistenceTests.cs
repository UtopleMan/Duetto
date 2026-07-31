using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class JsonHostKeyPersistenceTests
{
    private const string FpA = "ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og";
    private const string FpB = "mBc9XkQ2rT7wLpZa4VuHnE5yD8fGiJ1oKsM6NqR3SxY";

    private static string Key(string host = "example.com", int port = 22) =>
        HostKeyStore.MakeStoreKey("ssh-ed25519", host, port);

    private static JsonHostKeyPersistence MakePersistence(out Dictionary<string, string> storage)
    {
        var files = new Dictionary<string, string>();
        storage = files;
        return new JsonHostKeyPersistence(
            "hostkeys.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);
    }

    [Fact]
    public void LoadAll_returns_empty_when_file_missing()
    {
        var p = MakePersistence(out _);
        Assert.Empty(p.LoadAll());
    }

    [Fact]
    public void LoadAll_returns_empty_for_whitespace_content()
    {
        var files = new Dictionary<string, string> { ["hostkeys.json"] = "   " };
        var p = new JsonHostKeyPersistence(
            "hostkeys.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);

        Assert.Empty(p.LoadAll());
    }

    [Fact]
    public void LoadAll_returns_empty_for_corrupt_json()
    {
        var files = new Dictionary<string, string> { ["hostkeys.json"] = "{not valid" };
        var p = new JsonHostKeyPersistence(
            "hostkeys.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);

        Assert.Empty(p.LoadAll());
    }

    [Fact]
    public void Save_then_LoadAll_returns_saved_pin()
    {
        var p = MakePersistence(out _);
        var storeKey = Key();

        p.Save(storeKey, FpA);

        var all = p.LoadAll();
        Assert.Single(all);
        Assert.Equal(FpA, all[storeKey]);
    }

    [Fact]
    public void Save_multiple_pins_and_LoadAll_returns_all()
    {
        var p = MakePersistence(out _);
        p.Save(Key("a.example.com"), FpA);
        p.Save(Key("b.example.com"), FpB);

        var all = p.LoadAll();
        Assert.Equal(2, all.Count);
        Assert.Equal(FpA, all[Key("a.example.com")]);
        Assert.Equal(FpB, all[Key("b.example.com")]);
    }

    [Fact]
    public void Save_updates_existing_pin()
    {
        var p = MakePersistence(out _);
        var storeKey = Key();

        p.Save(storeKey, FpA);
        p.Save(storeKey, FpB);

        var all = p.LoadAll();
        Assert.Single(all);
        Assert.Equal(FpB, all[storeKey]);
    }

    [Fact]
    public void Remove_deletes_existing_pin()
    {
        var p = MakePersistence(out _);
        var storeKey = Key();
        p.Save(storeKey, FpA);

        p.Remove(storeKey);

        Assert.Empty(p.LoadAll());
    }

    [Fact]
    public void Remove_unknown_key_is_no_op()
    {
        var p = MakePersistence(out _);
        p.Save(Key(), FpA);

        p.Remove(Key("never-seen.example.com"));

        Assert.Single(p.LoadAll());
    }

    [Fact]
    public void Remove_leaves_other_pins_intact()
    {
        var p = MakePersistence(out _);
        p.Save(Key("a.example.com"), FpA);
        p.Save(Key("b.example.com"), FpB);

        p.Remove(Key("a.example.com"));

        var all = p.LoadAll();
        Assert.Single(all);
        Assert.Equal(FpB, all[Key("b.example.com")]);
    }

    [Fact]
    public void HostKeyStore_with_JsonPersistence_saves_new_pin_on_first_verify()
    {
        var p = MakePersistence(out _);
        var store = new HostKeyStore(p);

        store.Verify("example.com", 22, "ssh-ed25519", FpA);

        var all = p.LoadAll();
        Assert.Single(all);
        Assert.Equal(FpA, all[Key()]);
    }

    [Fact]
    public void HostKeyStore_with_JsonPersistence_reloads_pins_on_construction()
    {
        var p = MakePersistence(out _);
        p.Save(Key(), FpA);

        var store = new HostKeyStore(p);

        var trusted = store.Verify("example.com", 22, "ssh-ed25519", FpA);
        Assert.True(trusted);
    }

    [Fact]
    public void HostKeyStore_with_JsonPersistence_removes_pin_on_Forget()
    {
        var p = MakePersistence(out _);
        var store = new HostKeyStore(p);

        store.Verify("example.com", 22, "ssh-ed25519", FpA);
        store.Forget(Key());

        Assert.Empty(p.LoadAll());
    }

    [Fact]
    public void Attach_produces_JsonHostKeyPersistence()
    {
        using var tmp = new TempDir();
        var path = System.IO.Path.Combine(tmp.Path, "hostkeys.json");
        var p = JsonHostKeyPersistence.Attach(path);
        Assert.IsType<JsonHostKeyPersistence>(p);
    }

    [Fact]
    public void Keys_persist_in_OpenSSH_format()
    {
        var files = new Dictionary<string, string>();
        var p = new JsonHostKeyPersistence(
            "hostkeys.json",
            path => files.TryGetValue(path, out var v) ? v : null,
            (path, content) => files[path] = content);

        var storeKey = HostKeyStore.MakeStoreKey("ssh-ed25519", "example.com", 22);
        p.Save(storeKey, FpA);

        Assert.Contains("ssh-ed25519:[example.com]:22", files["hostkeys.json"]);
    }
}
