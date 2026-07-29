using Duetto.Core.Remote;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Security;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Tests HostKeyStore TOFU semantics.  Most tests call <see cref="HostKeyStore.Verify"/>
/// directly; the handler-path tests build a real <see cref="HostKeyEventArgs"/> from a
/// synthetic ed25519 public key.  No network sockets are opened.
/// </summary>
public class HostKeyStoreTests
{
    // SSH.NET's HostKeyEventArgs.FingerPrintSHA256 is the SHA-256 hash of the host key as
    // non-padded base64 WITHOUT a "SHA256:" prefix (the body of the ssh CLI's output),
    // e.g. "ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og".  Pins are stored verbatim in
    // that form — the Phase 3 hostkeys.json writer must keep the same format.
    private const string FpA = "ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og";
    private const string FpB = "mBc9XkQ2rT7wLpZa4VuHnE5yD8fGiJ1oKsM6NqR3SxY";
    private const string FpC = "Qw3ErT5yUi7oPa9sDf1gHj2kLz4xCv6bNm8JvC0XzAs";

    // ── first-use pins ───────────────────────────────────────────────────────

    [Fact]
    public void FirstUse_pins_the_fingerprint_and_returns_true()
    {
        var store = new HostKeyStore();
        var trusted = store.Verify("host1.example.com", "ssh-ed25519", FpA);
        Assert.True(trusted);
        Assert.Equal(FpA, store.GetPinned("ssh-ed25519:host1.example.com"));
    }

    [Fact]
    public void SecondUse_sameFingerprint_returns_true()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", FpA);

        var trusted = store.Verify("host1.example.com", "ssh-ed25519", FpA);
        Assert.True(trusted);
    }

    [Fact]
    public void ChangedFingerprint_throws_HostKeyChangedException_with_both_prints()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", FpA);

        var ex = Assert.Throws<HostKeyChangedException>(
            () => store.Verify("host1.example.com", "ssh-ed25519", FpB));

        Assert.Equal("host1.example.com", ex.Host);
        Assert.Equal(FpA, ex.OldFingerprint);
        Assert.Equal(FpB, ex.NewFingerprint);
    }

    [Fact]
    public void ChangedFingerprint_does_not_update_stored_pin()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", FpA);

        Assert.Throws<HostKeyChangedException>(
            () => store.Verify("host1.example.com", "ssh-ed25519", FpB));

        // original pin must still be stored
        Assert.Equal(FpA, store.GetPinned("ssh-ed25519:host1.example.com"));
    }

    [Fact]
    public void DifferentHosts_are_pinned_independently()
    {
        var store = new HostKeyStore();
        store.Verify("host-a.example.com", "ssh-ed25519", FpA);
        store.Verify("host-b.example.com", "ssh-ed25519", FpB);

        Assert.Equal(FpA, store.GetPinned("ssh-ed25519:host-a.example.com"));
        Assert.Equal(FpB, store.GetPinned("ssh-ed25519:host-b.example.com"));
    }

    [Fact]
    public void DifferentAlgorithms_same_host_are_pinned_independently()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", FpA);
        store.Verify("host1.example.com", "ecdsa-sha2-nistp256", FpB);

        Assert.Equal(FpA, store.GetPinned("ssh-ed25519:host1.example.com"));
        Assert.Equal(FpB, store.GetPinned("ecdsa-sha2-nistp256:host1.example.com"));
    }

    [Fact]
    public void Forget_removes_pin_so_next_verify_repins()
    {
        var store = new HostKeyStore();
        store.Verify("host1.example.com", "ssh-ed25519", FpA);

        store.Forget("ssh-ed25519:host1.example.com");
        Assert.Null(store.GetPinned("ssh-ed25519:host1.example.com"));

        // re-pin with a NEW fingerprint — should succeed (TOFU again)
        var trusted = store.Verify("host1.example.com", "ssh-ed25519", FpC);
        Assert.True(trusted);
        Assert.Equal(FpC, store.GetPinned("ssh-ed25519:host1.example.com"));
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
                ["ssh-ed25519:host1.example.com"] = FpA
            });

        var store = new HostKeyStore(persistence);

        // Presenting the same fingerprint must pass
        var trusted = store.Verify("host1.example.com", "ssh-ed25519", FpA);
        Assert.True(trusted);
    }

    [Fact]
    public void Loaded_persisted_pin_rejects_changed_fingerprint()
    {
        var persistence = new DictionaryHostKeyPersistence(
            new Dictionary<string, string>
            {
                ["ssh-ed25519:host1.example.com"] = FpA
            });

        var store = new HostKeyStore(persistence);

        Assert.Throws<HostKeyChangedException>(
            () => store.Verify("host1.example.com", "ssh-ed25519", FpB));
    }

    [Fact]
    public void Persists_new_pin_on_first_use()
    {
        var persistence = new DictionaryHostKeyPersistence();
        var store = new HostKeyStore(persistence);

        store.Verify("host1.example.com", "ssh-ed25519", FpA);

        Assert.Equal(FpA, persistence.GetPinned("ssh-ed25519:host1.example.com"));
    }

    [Fact]
    public void Forget_calls_persistence_remove()
    {
        var persistence = new DictionaryHostKeyPersistence(
            new Dictionary<string, string>
            {
                ["ssh-ed25519:host1.example.com"] = FpA
            });

        var store = new HostKeyStore(persistence);
        store.Forget("ssh-ed25519:host1.example.com");

        Assert.Null(persistence.GetPinned("ssh-ed25519:host1.example.com"));
    }

    // ── HandleHostKeyReceived (the real SSH.NET event path) ──────────────────

    [Fact]
    public void Handler_first_use_pins_real_fingerprint_and_sets_CanTrust()
    {
        var store = new HostKeyStore();
        var args = MakeRealHostKeyArgs();
        var sender = new FakeHostKeySender("host1.example.com");

        store.HandleHostKeyReceived(sender, args);

        Assert.True(args.CanTrust);
        Assert.Equal(
            args.FingerPrintSHA256,
            store.GetPinned(args.HostKeyName + ":host1.example.com"));
    }

    [Fact]
    public void Handler_changed_pin_throws_HostKeyChangedException()
    {
        var store = new HostKeyStore();
        var args = MakeRealHostKeyArgs();
        var sender = new FakeHostKeySender("host1.example.com");

        // Pin a different fingerprint for the same host+algorithm first.
        store.Verify("host1.example.com", args.HostKeyName, FpB);

        var ex = Assert.Throws<HostKeyChangedException>(
            () => store.HandleHostKeyReceived(sender, args));

        Assert.Equal("host1.example.com", ex.Host);
        Assert.Equal(FpB, ex.OldFingerprint);
        Assert.Equal(args.FingerPrintSHA256, ex.NewFingerprint);
        Assert.False(args.CanTrust); // must NOT be trusted
    }

    /// <summary>
    /// Builds a real <see cref="HostKeyEventArgs"/> from a synthetic (non-random) ed25519
    /// public key encoded in SSH wire format: <c>string "ssh-ed25519" + string(32-byte key)</c>.
    /// </summary>
    private static HostKeyEventArgs MakeRealHostKeyArgs()
    {
        var algo = System.Text.Encoding.ASCII.GetBytes("ssh-ed25519");
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
            key[i] = (byte)(i + 1); // deterministic, first byte < 0x80

        var blob = new byte[4 + algo.Length + 4 + key.Length];
        blob[3] = (byte)algo.Length;
        algo.CopyTo(blob, 4);
        blob[4 + algo.Length + 3] = (byte)key.Length;
        key.CopyTo(blob, 8 + algo.Length);

        var ed25519 = new ED25519Key(new SshKeyData(blob));
        return new HostKeyEventArgs(new KeyHostAlgorithm("ssh-ed25519", ed25519));
    }
}

// ── fakes / helpers ───────────────────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="IBaseClient"/> so <see cref="HostKeyStore.HandleHostKeyReceived"/> can
/// read the host name from the event sender, exactly as it does with a real SftpClient.
/// </summary>
internal sealed class FakeHostKeySender : IBaseClient
{
    private readonly string _host;

    public FakeHostKeySender(string host) => _host = host;

    public Renci.SshNet.ConnectionInfo ConnectionInfo =>
        new(_host, "unused", new PasswordAuthenticationMethod("u", "p"));

    // ── unused IBaseClient members ────────────────────────────────────────
    public bool IsConnected => false;
    public TimeSpan KeepAliveInterval { get => TimeSpan.Zero; set { } }
    public void Connect() { }
    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Disconnect() { }
    public void SendKeepAlive() { }
#pragma warning disable 67
    public event EventHandler<ExceptionEventArgs>? ErrorOccurred;
    public event EventHandler<HostKeyEventArgs>? HostKeyReceived;
    public event EventHandler<SshIdentificationEventArgs>? ServerIdentificationReceived;
#pragma warning restore 67
    public void Dispose() { }
}

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
