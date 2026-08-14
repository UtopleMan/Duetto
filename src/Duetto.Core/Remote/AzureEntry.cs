namespace Duetto.Core.Remote;

public sealed record AzureEntry(
    string Name,
    string FullName,
    bool IsDirectory,
    bool IsReadOnly,
    long Length,
    DateTime LastWriteTimeUtc);
