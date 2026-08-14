namespace Duetto.Core.Remote;

public sealed record S3Entry(
    string Name,
    string FullName,
    bool IsDirectory,
    bool IsReadOnly,
    long Length,
    DateTime LastWriteTimeUtc);
