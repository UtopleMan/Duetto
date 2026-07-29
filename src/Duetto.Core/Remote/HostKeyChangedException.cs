namespace Duetto.Core.Remote;

/// <summary>
/// Raised by <see cref="HostKeyStore"/> when the host key presented during an SSH handshake
/// differs from the fingerprint that was previously pinned for that host, or when the host
/// presents a key using a different algorithm than the one that was previously pinned.
/// This indicates a potential man-in-the-middle attack, a legitimate key rotation, or an
/// algorithm-substitution attempt.
/// The UI layer should surface both fingerprints to the user and require explicit re-trust.
/// </summary>
public sealed class HostKeyChangedException : Exception
{
    /// <summary>The hostname/IP whose key has changed.</summary>
    public string Host { get; }

    /// <summary>The SHA-256 fingerprint that was previously trusted (stored form).</summary>
    public string OldFingerprint { get; }

    /// <summary>The SHA-256 fingerprint presented by the server during this handshake.</summary>
    public string NewFingerprint { get; }

    /// <summary>
    /// The algorithm name of the key presented by the server (e.g. <c>"ssh-ed25519"</c>).
    /// May differ from the algorithm of the stored pin when an algorithm-substitution is detected.
    /// Phase 4: pass to the re-trust dialog so it can call
    /// <see cref="HostKeyStore.Forget"/> with <see cref="StoreKey"/> and then re-pin.
    /// </summary>
    public string AlgorithmName { get; }

    /// <summary>
    /// The exact key used in the pin map (<c>"algo:[host]:port"</c>) that triggered this
    /// exception (for a same-algo change) or any existing pin for the host+port (for an
    /// algorithm-substitution).  Phase 4: pass to <see cref="HostKeyStore.Forget"/> to clear
    /// the old trust entry before re-pinning.
    /// </summary>
    public string StoreKey { get; }

    /// <summary>
    /// Initialises a new <see cref="HostKeyChangedException"/>.
    /// </summary>
    public HostKeyChangedException(
        string host,
        string oldFingerprint,
        string newFingerprint,
        string algorithmName,
        string storeKey)
        : base($"Host key for '{host}' has changed. " +
               $"Old: {oldFingerprint}  New: {newFingerprint}  Algo: {algorithmName}")
    {
        Host = host;
        OldFingerprint = oldFingerprint;
        NewFingerprint = newFingerprint;
        AlgorithmName = algorithmName;
        StoreKey = storeKey;
    }
}
