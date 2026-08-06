namespace Duetto.Core.Remote;

// Thin DTO returned by IAzureClientAdapter so the fake in tests stays small and the provider never
// sees Azure.Storage.Blobs types. FullName is the provider-local path ("/container/blob"). Length is
// -1 for directories (container / prefix). LastWriteTimeUtc is UTC (default for synthetic prefixes).
public sealed record AzureEntry(
    string Name,
    string FullName,
    bool IsDirectory,
    bool IsReadOnly,
    long Length,
    DateTime LastWriteTimeUtc);
