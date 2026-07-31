namespace Duetto.Core.FileSystem;

// Callers gate features on these (disable a command, pick a transfer strategy) rather than calling
// and catching — optional methods still throw NotSupportedException as a backstop. Odd backends
// (S3, WebDAV) return a leaner descriptor and the app degrades gracefully.
public sealed record FileSystemCapabilities
{
    // When false, move falls back to copy + delete.
    public required bool CanRename { get; init; }

    // Object stores that key on prefixes cannot create an empty directory.
    public required bool CanCreateEmptyDir { get; init; }

    public required bool CanCreateFile { get; init; }
    public required bool CanDelete { get; init; }

    // When false, delete is permanent.
    public required bool HasTrash { get; init; }

    public required bool HasPermissions { get; init; }

    public required bool PreservesMTime { get; init; }

    // Supports write-to-temp then atomic rename (resumable ".part" transfers).
    public required bool AtomicRename { get; init; }

    public required bool CanWatch { get; init; }

    public required bool ReportsCapacity { get; init; }

    public required bool SupportsSearch { get; init; }

    public required bool CaseSensitive { get; init; }
    public required char Separator { get; init; }

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
