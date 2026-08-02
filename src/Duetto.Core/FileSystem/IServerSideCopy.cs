namespace Duetto.Core.FileSystem;

// Optional capability: copy `source` to `dest` entirely on the backend (no bytes through the
// client). The caller guarantees, via IBackendIdentity, that both paths share this provider's
// copy domain (same host + share). Reports per-step bytes copied. Returns false when the server
// does not support server-side copy, so the caller falls back to streaming.
public interface IServerSideCopy
{
    bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token);
}
