using Renci.SshNet.Common;

namespace Duetto.Core.Remote;

/// <summary>
/// Trust-On-First-Use (TOFU) host-key store.
///
/// <para>
/// On the first connection to a host, the server's SHA-256 fingerprint is pinned in memory
/// (and optionally persisted via the <see cref="IHostKeyPersistence"/> seam).  On subsequent
/// connections the presented fingerprint is compared: if it matches, <see cref="CanTrust"/> is
/// set to <see langword="true"/>; if it differs, <see cref="HostKeyChangedException"/> is thrown.
/// </para>
///
/// <para>
/// Wire this to an <see cref="Renci.SshNet.IBaseClient.HostKeyReceived"/> event before calling
/// Connect.  A single <see cref="HostKeyStore"/> instance may be shared across multiple
/// <see cref="SftpConnection"/> objects; the store is keyed by <c>algo:host</c>.
/// </para>
///
/// Phase 3 note: swap out the default <see cref="NullHostKeyPersistence"/> with a
/// <c>JsonHostKeyPersistence</c> implementation to wire <c>hostkeys.json</c>.
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
    /// Event handler compatible with <see cref="Renci.SshNet.IBaseClient.HostKeyReceived"/>.
    /// Wire this before calling Connect:
    /// <code>
    ///   client.HostKeyReceived += hostKeyStore.HandleHostKeyReceived;
    /// </code>
    /// </summary>
    /// <exception cref="HostKeyChangedException">
    /// Thrown when the presented fingerprint differs from the one previously pinned for this host.
    /// </exception>
    public void HandleHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var host = sender is Renci.SshNet.IBaseClient c ? c.ConnectionInfo.Host : "<unknown>";

        // SSH.NET initialises CanTrust to true. Drop trust before verifying so a thrown
        // HostKeyChangedException can never leave the presented key trusted.
        e.CanTrust = false;
        e.CanTrust = Verify(host, e.HostKeyName, e.FingerPrintSHA256);
    }

    /// <summary>
    /// Core TOFU logic, callable independently of SSH.NET event args (useful for testing).
    /// </summary>
    /// <param name="host">The remote hostname or IP.</param>
    /// <param name="algoName">The host-key algorithm name (e.g. <c>"ssh-ed25519"</c>).</param>
    /// <param name="fingerprint">The SHA-256 fingerprint presented by the server.</param>
    /// <returns>
    /// <see langword="true"/> when the fingerprint is trusted (first-use pin or unchanged).
    /// </returns>
    /// <exception cref="HostKeyChangedException">
    /// Thrown when the fingerprint differs from the previously pinned value.
    /// </exception>
    public bool Verify(string host, string algoName, string fingerprint)
    {
        var storeKey = algoName + ":" + host;

        lock (_lock)
        {
            if (_pins.TryGetValue(storeKey, out var stored))
            {
                if (stored == fingerprint)
                    return true;

                throw new HostKeyChangedException(host, stored, fingerprint);
            }

            // First use: pin and trust.
            _pins[storeKey] = fingerprint;
            _persistence.Save(storeKey, fingerprint);
            return true;
        }
    }

    /// <summary>
    /// Returns the stored fingerprint for the given store key (<c>"algo:host"</c>), or
    /// <see langword="null"/> when no key has been pinned yet.  Primarily for testing.
    /// </summary>
    public string? GetPinned(string storeKey)
    {
        lock (_lock)
            return _pins.TryGetValue(storeKey, out var fp) ? fp : null;
    }

    /// <summary>
    /// Removes the pin for the given store key, allowing the next connection to re-pin.
    /// Intended for "forget this host" UI flows.
    /// </summary>
    public void Forget(string storeKey)
    {
        lock (_lock)
        {
            _pins.Remove(storeKey);
            _persistence.Remove(storeKey);
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
