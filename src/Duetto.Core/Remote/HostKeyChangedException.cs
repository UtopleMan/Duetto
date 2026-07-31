namespace Duetto.Core.Remote;

// A changed or algorithm-substituted host key indicates a potential man-in-the-middle
// attack, a legitimate key rotation, or an algorithm-substitution attempt. The UI layer
// must surface both fingerprints to the user and require explicit re-trust — never trust
// the new key silently.
public sealed class HostKeyChangedException : Exception
{
    public string Host { get; }

    public string OldFingerprint { get; }

    public string NewFingerprint { get; }

    // May differ from the stored pin's algorithm when an algorithm-substitution is detected.
    public string AlgorithmName { get; }

    // The exact pin-map key ("algo:[host]:port") that triggered this exception: the matching
    // pin for a same-algo change, or any existing host+port pin for an algorithm-substitution.
    public string StoreKey { get; }

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
