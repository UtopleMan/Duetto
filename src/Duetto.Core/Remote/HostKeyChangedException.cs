namespace Duetto.Core.Remote;

/// <summary>
/// Raised by <see cref="HostKeyStore"/> when the host key presented during an SSH handshake
/// differs from the fingerprint that was previously pinned for that host.
/// This indicates a potential man-in-the-middle attack or a legitimate key rotation.
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
    /// Initialises a new <see cref="HostKeyChangedException"/>.
    /// </summary>
    public HostKeyChangedException(string host, string oldFingerprint, string newFingerprint)
        : base($"Host key for '{host}' has changed. " +
               $"Old: {oldFingerprint}  New: {newFingerprint}")
    {
        Host = host;
        OldFingerprint = oldFingerprint;
        NewFingerprint = newFingerprint;
    }
}
