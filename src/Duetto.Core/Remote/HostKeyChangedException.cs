namespace Duetto.Core.Remote;

public sealed class HostKeyChangedException : Exception
{
    public string Host { get; }

    public string OldFingerprint { get; }

    public string NewFingerprint { get; }

    public string AlgorithmName { get; }

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
