namespace Duetto.Core.Remote;

public sealed record SftpEntry(
    string Name,
    string FullName,
    bool IsDirectory,
    bool IsSymbolicLink,
    long Length,
    DateTime LastWriteTimeUtc,
    bool OwnerCanRead,
    bool OwnerCanWrite,
    bool OwnerCanExecute,
    bool GroupCanRead,
    bool GroupCanWrite,
    bool GroupCanExecute,
    bool OthersCanRead,
    bool OthersCanWrite,
    bool OthersCanExecute);
