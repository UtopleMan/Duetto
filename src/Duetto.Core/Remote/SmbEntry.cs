namespace Duetto.Core.Remote;

// Thin DTO returned by ISmbClientAdapter so the fake in tests stays small and the provider
// never sees SMBLibrary types. Length is -1 for directories. LastWriteTimeUtc is UTC.
public sealed record SmbEntry(
    string Name,
    string FullName,
    bool IsDirectory,
    bool IsReadOnly,
    long Length,
    DateTime LastWriteTimeUtc);
