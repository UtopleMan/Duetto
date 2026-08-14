namespace Duetto.Core.FileSystem;

public interface IBackendIdentity
{
    string? BackendKey(string path);
}
