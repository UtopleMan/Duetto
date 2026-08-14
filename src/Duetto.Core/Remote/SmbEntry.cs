namespace Duetto.Core.Remote;

public sealed record SmbEntry(
    string Name,
    string FullName,
    bool IsDirectory,
    bool IsReadOnly,
    long Length,
    DateTime LastWriteTimeUtc);
