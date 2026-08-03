namespace Duetto.Core.Remote;

// Thin DTO returned by IS3ClientAdapter so the fake in tests stays small and the provider never
// sees AWSSDK types. FullName is the provider-local path ("/bucket/key"). Length is -1 for
// directories (bucket / prefix). LastWriteTimeUtc is UTC (default for synthetic prefixes).
public sealed record S3Entry(
    string Name,
    string FullName,
    bool IsDirectory,
    bool IsReadOnly,
    long Length,
    DateTime LastWriteTimeUtc);
