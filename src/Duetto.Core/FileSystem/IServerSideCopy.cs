namespace Duetto.Core.FileSystem;

public interface IServerSideCopy
{
    bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token);
}
