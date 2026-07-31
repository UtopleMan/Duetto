namespace Duetto.Core.Remote;

// A thin subset of SFTP metadata so neither the real adapter nor the test fake has to
// implement SSH.NET's ~30-member ISftpFile interface.
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
