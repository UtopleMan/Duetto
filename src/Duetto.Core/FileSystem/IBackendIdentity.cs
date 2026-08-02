namespace Duetto.Core.FileSystem;

// Optional capability: identify the server-side rename/copy domain that contains `path`.
// Two providers that return equal, NON-NULL keys for their respective paths address the same
// backend location, where a native rename (and, for IServerSideCopy providers, a server-side
// copy) between those two paths is valid and stays entirely server-side. Null means the path
// has no such domain (e.g. an SMB share-list root) or the provider opts out.
public interface IBackendIdentity
{
    string? BackendKey(string path);
}
