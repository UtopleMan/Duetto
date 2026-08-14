namespace Duetto.Core.FileSystem;

public sealed record FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }

    public required long SizeBytes { get; init; }

    public required string TypeLabel { get; init; }
    public required DateTime ModifiedUtc { get; init; }

    public required string UnixPermissions { get; init; }

    public required string AccessSummary { get; init; }
}
