namespace Duet.Core.FileSystem;

/// <summary>One row in a pane: file or directory with display-ready metadata.</summary>
public sealed record FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }

    /// <summary>Bytes for files; -1 for directories (rendered as "—").</summary>
    public required long SizeBytes { get; init; }

    public required string TypeLabel { get; init; }
    public required DateTime ModifiedUtc { get; init; }

    /// <summary>"rwxr-xr-x" style string on Unix, "" on Windows.</summary>
    public required string UnixPermissions { get; init; }

    /// <summary>Short access summary: "RW" or "R".</summary>
    public required string AccessSummary { get; init; }
}
