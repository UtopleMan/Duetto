namespace Duetto.Core.FileSystem;

/// <summary>
/// What an <see cref="IFileSystemProvider"/> can do. Callers gate features on these
/// (disable a command, pick a transfer strategy) rather than calling and catching —
/// optional methods still throw <see cref="NotSupportedException"/> as a backstop.
/// Odd backends (S3, WebDAV) return a leaner descriptor and the app degrades gracefully.
/// </summary>
public sealed record FileSystemCapabilities
{
    /// <summary>Native in-place rename. When false, move falls back to copy + delete.</summary>
    public required bool CanRename { get; init; }

    /// <summary>Can create an empty directory (object stores that key on prefixes cannot).</summary>
    public required bool CanCreateEmptyDir { get; init; }

    public required bool CanCreateFile { get; init; }
    public required bool CanDelete { get; init; }

    /// <summary>Delete goes to a recoverable trash. When false, delete is permanent.</summary>
    public required bool HasTrash { get; init; }

    /// <summary>Exposes Unix permission bits (drives the perms column).</summary>
    public required bool HasPermissions { get; init; }

    /// <summary>Modified time can be set on a written file (mtime-preserving copy).</summary>
    public required bool PreservesMTime { get; init; }

    /// <summary>Supports write-to-temp then atomic rename (resumable ".part" transfers).</summary>
    public required bool AtomicRename { get; init; }

    /// <summary>Emits live change notifications (a FileSystemWatcher equivalent).</summary>
    public required bool CanWatch { get; init; }

    /// <summary>Reports free/total capacity for the volume chip's usage bar.</summary>
    public required bool ReportsCapacity { get; init; }

    /// <summary>Recursive enumerate + content read for search.</summary>
    public required bool SupportsSearch { get; init; }

    public required bool CaseSensitive { get; init; }
    public required char Separator { get; init; }

    /// <summary>A full local disk: every capability on, native path separator.</summary>
    public static FileSystemCapabilities LocalDisk { get; } = new()
    {
        CanRename = true,
        CanCreateEmptyDir = true,
        CanCreateFile = true,
        CanDelete = true,
        HasTrash = true,
        HasPermissions = !OperatingSystem.IsWindows(),
        PreservesMTime = true,
        AtomicRename = true,
        CanWatch = true,
        ReportsCapacity = true,
        SupportsSearch = true,
        CaseSensitive = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS(),
        Separator = Path.DirectorySeparatorChar,
    };
}
