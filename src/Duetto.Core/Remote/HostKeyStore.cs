using Renci.SshNet.Common;

namespace Duetto.Core.Remote;

/// <summary>
/// Trust-On-First-Use (TOFU) host-key store.
///
/// <para>
/// On the first connection to a host+port, the server's SHA-256 fingerprint is pinned in
/// memory (and optionally persisted via the <see cref="IHostKeyPersistence"/> seam).  On
/// subsequent connections the presented fingerprint is compared: if it matches,
/// <see cref="HostKeyEventArgs.CanTrust"/> is set to <see langword="true"/>; if it differs
/// (or the host presents a different algorithm than the stored pin), a
/// <see cref="HostKeyChangedException"/> is thrown.
/// </para>
///
/// <para>
/// Wire this to an <see cref="Renci.SshNet.IBaseClient.HostKeyReceived"/> event before calling
/// Connect.  A single <see cref="HostKeyStore"/> instance may be shared across multiple
/// <see cref="SftpConnection"/> objects; the store is keyed by
/// <c>"algo:[host]:port"</c> (OpenSSH-style, e.g. <c>"ssh-ed25519:[example.com]:22"</c>).
/// Two servers behind the same hostname on different ports receive independent pins.
/// </para>
///
/// Phase 3 note: swap out the default <see cref="NullHostKeyPersistence"/> with a
/// <c>JsonHostKeyPersistence</c> implementation to wire <c>hostkeys.json</c>.
/// The <c>hostkeys.json</c> writer must use the same <c>"algo:[host]:port"</c> key format.
/// </summary>
public sealed class HostKeyStore
{
    private readonly Dictionary<string, string> _pins = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHostKeyPersistence _persistence;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a <see cref="HostKeyStore"/> backed by the supplied persistence provider.
    /// Use <see cref="NullHostKeyPersistence"/> (the default) for an in-memory-only store.
    /// </summary>
    public HostKeyStore(IHostKeyPersistence? persistence = null)
    {
        _persistence = persistence ?? new NullHostKeyPersistence();

        // Load any previously persisted pins.
        foreach (var (key, fp) in _persistence.LoadAll())
            _pins[key] = fp;
    }

    /// <summary>
    /// Builds the store key for a given algorithm, host, and port.
    /// Format: <c>"algo:[host]:port"</c> (OpenSSH-style), e.g.
    /// <c>"ssh-ed25519:[example.com]:22"</c>.
    /// Phase 3: <c>hostkeys.json</c> must persist and load keys in this exact format.
    /// </summary>
    public static string MakeStoreKey(string algoName, string host, int port) =>
        $"{algoName}:[{host}]:{port}";

    /// <summary>
    /// Event handler compatible with <see cref="Renci.SshNet.IBaseClient.HostKeyReceived"/>.
    /// Wire this before calling Connect:
    /// <code>
    ///   client.HostKeyReceived += hostKeyStore.HandleHostKeyReceived;
    /// </code>
    /// </summary>
    /// <exception cref="HostKeyChangedException">
    /// Thrown when the presented fingerprint or algorithm differs from the one previously
    /// pinned for this host+port.
    /// </exception>
    public void HandleHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        string host;
        int port;
        if (sender is Renci.SshNet.IBaseClient c)
        {
            host = c.ConnectionInfo.Host;
            port = c.ConnectionInfo.Port;
        }
        else
        {
            host = "<unknown>";
            port = 22;
        }

        // SSH.NET initialises CanTrust to true. Drop trust before verifying so a thrown
        // HostKeyChangedException can never leave the presented key trusted.
        e.CanTrust = false;
        e.CanTrust = Verify(host, port, e.HostKeyName, e.FingerPrintSHA256);
    }

    /// <summary>
    /// Core TOFU logic, callable independently of SSH.NET event args (useful for testing).
    /// </summary>
    /// <param name="host">The remote hostname or IP.</param>
    /// <param name="port">The SSH port (distinguishes two servers on the same hostname).</param>
    /// <param name="algoName">The host-key algorithm name (e.g. <c>"ssh-ed25519"</c>).</param>
    /// <param name="fingerprint">The SHA-256 fingerprint presented by the server.</param>
    /// <returns>
    /// <see langword="true"/> when the fingerprint is trusted (first-use pin or unchanged).
    /// </returns>
    /// <exception cref="HostKeyChangedException">
    /// Thrown when the fingerprint differs from the previously pinned value, or when any pin
    /// exists for the host+port but none for the presented algorithm (algorithm substitution).
    /// </exception>
    public bool Verify(string host, int port, string algoName, string fingerprint)
    {
        var storeKey = MakeStoreKey(algoName, host, port);

        lock (_lock)
        {
            if (_pins.TryGetValue(storeKey, out var stored))
            {
                if (stored == fingerprint)
                    return true;

                // Same algorithm, changed fingerprint: classic key-change.
                throw new HostKeyChangedException(host, stored, fingerprint, algoName, storeKey);
            }

            // Check for algorithm substitution: some other algorithm is pinned for this host+port.
            // A genuine first contact has no pins at all; an algorithm switch has at least one.
            var hostSuffix = $":[{host}]:{port}";
            var existingPin = _pins
                .FirstOrDefault(kv => kv.Key.EndsWith(hostSuffix, StringComparison.OrdinalIgnoreCase));

            if (existingPin.Key is not null)
            {
                // Algorithm substitution: raise HostKeyChangedException with the old pin's
                // fingerprint so the Phase 4 dialog can compare.
                throw new HostKeyChangedException(
                    host,
                    oldFingerprint: existingPin.Value,
                    newFingerprint: fingerprint,
                    algorithmName: algoName,
                    storeKey: existingPin.Key);
            }

            // Genuine first contact: pin and trust.
            _pins[storeKey] = fingerprint;
            _persistence.Save(storeKey, fingerprint);
            return true;
        }
    }

    /// <summary>
    /// Returns the stored fingerprint for the given store key
    /// (<c>"algo:[host]:port"</c>), or <see langword="null"/> when no key has been pinned yet.
    /// Primarily for testing.
    /// </summary>
    public string? GetPinned(string storeKey)
    {
        lock (_lock)
            return _pins.TryGetValue(storeKey, out var fp) ? fp : null;
    }

    /// <summary>
    /// Removes the pin for the given store key, allowing the next connection to re-pin.
    /// Intended for "forget this host" UI flows (Phase 4).
    /// </summary>
    /// <param name="storeKey">
    /// The exact <c>"algo:[host]:port"</c> key — use <see cref="HostKeyChangedException.StoreKey"/>
    /// from the caught exception to identify which pin to clear.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key was present and removed;
    /// <see langword="false"/> when no such pin existed.
    /// </returns>
    public bool Forget(string storeKey)
    {
        lock (_lock)
        {
            if (!_pins.Remove(storeKey))
                return false;

            _persistence.Remove(storeKey);
            return true;
        }
    }
}

/// <summary>
/// Seam for persisting host-key pins across sessions.
/// Phase 3 provides a <c>JsonHostKeyPersistence</c> that reads/writes <c>hostkeys.json</c>.
/// </summary>
public interface IHostKeyPersistence
{
    /// <summary>Returns all previously persisted (storeKey → fingerprint) pairs.</summary>
    IReadOnlyDictionary<string, string> LoadAll();

    /// <summary>Persists or updates the fingerprint for the given <paramref name="storeKey"/>.</summary>
    void Save(string storeKey, string fingerprint);

    /// <summary>Removes the entry for the given <paramref name="storeKey"/>, if present.</summary>
    void Remove(string storeKey);
}

/// <summary>No-op persistence — pins survive only for the lifetime of the process.</summary>
public sealed class NullHostKeyPersistence : IHostKeyPersistence
{
    public IReadOnlyDictionary<string, string> LoadAll() =>
        new Dictionary<string, string>();

    public void Save(string storeKey, string fingerprint) { }

    public void Remove(string storeKey) { }
}
