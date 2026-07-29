using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Tests HostKeyStore TOFU semantics.  All tests call <see cref="HostKeyStore.Verify"/> so
/// that no SSH.NET objects (or network sockets) are required.
/// </summary>
public class HostKeyStoreTests
{
    // ── first-use pins ───────────────────────────────────────────────────────

    [Fact]
    public void FirstUse_pins_the_fingerprint_and_returns_true()
    {
        var store = new HostKeyStore();
        var trusted = store.Verify("host1.example.com", "ssh-ed25519", "SHA256:abc123");
        Assert.True(trusted);
        Assert.Equal("SHA256:abc123", store.GetPinned("ssh-ed25519:host1.example.com"));
    }

    [Fact]
    public void SecondUse_sameFingerprint_returns_true()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", "SHA256:abc123");

        var trusted = store.Verify("host1.example.com", "ssh-ed25519", "SHA256:abc123");
        Assert.True(trusted);
    }

    [Fact]
    public void ChangedFingerprint_throws_HostKeyChangedException_with_both_prints()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", "SHA256:original");

        var ex = Assert.Throws<HostKeyChangedException>(
            () => store.Verify("host1.example.com", "ssh-ed25519", "SHA256:changed"));

        Assert.Equal("host1.example.com", ex.Host);
        Assert.Equal("SHA256:original", ex.OldFingerprint);
        Assert.Equal("SHA256:changed", ex.NewFingerprint);
    }

    [Fact]
    public void ChangedFingerprint_does_not_update_stored_pin()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", "SHA256:original");

        Assert.Throws<HostKeyChangedException>(
            () => store.Verify("host1.example.com", "ssh-ed25519", "SHA256:changed"));

        // original pin must still be stored
        Assert.Equal("SHA256:original", store.GetPinned("ssh-ed25519:host1.example.com"));
    }

    [Fact]
    public void DifferentHosts_are_pinned_independently()
    {
        var store = new HostKeyStore();
        store.Verify("host-a.example.com", "ssh-ed25519", "SHA256:aaa");
        store.Verify("host-b.example.com", "ssh-ed25519", "SHA256:bbb");

        Assert.Equal("SHA256:aaa", store.GetPinned("ssh-ed25519:host-a.example.com"));
        Assert.Equal("SHA256:bbb", store.GetPinned("ssh-ed25519:host-b.example.com"));
    }

    [Fact]
    public void DifferentAlgorithms_same_host_are_pinned_independently()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", "SHA256:ed");
        store.Verify("host1.example.com", "ecdsa-sha2-nistp256", "SHA256:ec");

        Assert.Equal("SHA256:ed", store.GetPinned("ssh-ed25519:host1.example.com"));
        Assert.Equal("SHA256:ec", store.GetPinned("ecdsa-sha2-nistp256:host1.example.com"));
    }

    [Fact]
    public void Forget_removes_pin_so_next_verify_repins()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", "SHA256:abc123");

        store.Forget("ssh-ed25519:host1.example.com");
        Assert.Null(store.GetPinned("ssh-ed25519:host1.example.com"));

        // re-pin with a NEW fingerprint — should succeed (TOFU again)
        var trusted = store.Verify("host1.example.com", "ssh-ed25519", "SHA256:newkey");
        Assert.True(trusted);
        Assert.Equal("SHA256:newkey", store.GetPinned("ssh-ed25519:host1.example.com"));
    }

    [Fact]
    public void GetPinned_returns_null_for_unknown_host()
    {
        var store = new HostKeyStore();
        Assert.Null(store.GetPinned("ssh-ed25519:unknown.example.com"));
    }

    // ── persistence seam ─────────────────────────────────────────────────────

    [Fact]
    public void Loads_persisted_pins_at_construction()
    {
        var persistence = new DictionaryHostKeyPersistence(
            new Dictionary<string, string>
            {
                ["ssh-ed25519:host1.example.com"] = "SHA256:preloaded"
            });

        var store = new HostKeyStore(persistence);

        // Presenting the same fingerprint must pass
        var trusted = store.Verify("host1.example.com", "ssh-ed25519", "SHA256:preloaded");
        Assert.True(trusted);
    }

    [Fact]
    public void Loaded_persisted_pin_rejects_changed_fingerprint()
    {
        var persistence = new DictionaryHostKeyPersistence(
            new Dictionary<string, string>
            {
                ["ssh-ed25519:host1.example.com"] = "SHA256:preloaded"
            });

        var store = new HostKeyStore(persistence);

        Assert.Throws<HostKeyChangedException>(
            () => store.Verify("host1.example.com", "ssh-ed25519", "SHA256:different"));
    }

    [Fact]
    public void Persists_new_pin_on_first_use()
    {
        var persistence = new DictionaryHostKeyPersistence();
        var store = new HostKeyStore(persistence);

        store.Verify("host1.example.com", "ssh-ed25519", "SHA256:abc123");

        Assert.Equal("SHA256:abc123", persistence.GetPinned("ssh-ed25519:host1.example.com"));
    }

    [Fact]
    public void Forget_calls_persistence_remove()
    {
        var persistence = new DictionaryHostKeyPersistence(
            new Dictionary<string, string>
            {
                ["ssh-ed25519:host1.example.com"] = "SHA256:abc"
            });

        var store = new HostKeyStore(persistence);
        store.Forget("ssh-ed25519:host1.example.com");

        Assert.Null(persistence.GetPinned("ssh-ed25519:host1.example.com"));
    }
}

// ── fakes / helpers ───────────────────────────────────────────────────────────

/// <summary>
/// In-memory <see cref="IHostKeyPersistence"/> for test assertions.
/// </summary>
internal sealed class DictionaryHostKeyPersistence : IHostKeyPersistence
{
    private readonly Dictionary<string, string> _store;

    public DictionaryHostKeyPersistence(Dictionary<string, string>? initial = null)
        => _store = initial is not null
            ? new Dictionary<string, string>(initial)
            : new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> LoadAll() => _store;
    public void Save(string key, string fp) => _store[key] = fp;
    public void Remove(string key) => _store.Remove(key);
    public string? GetPinned(string key) => _store.TryGetValue(key, out var v) ? v : null;
}
