namespace Duetto.Core.Remote;

/// <summary>
/// A thin value record that carries the SFTP metadata that <see cref="SftpFileSystemProvider"/>
/// needs from a directory listing or a single-path stat.  The real adapter populates this from
/// <c>ISftpFile</c>; the test fake populates it from an in-memory tree.  Neither side is
/// required to implement SSH.NET's ~30-member <c>ISftpFile</c> interface.
/// </summary>
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
