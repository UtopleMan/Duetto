namespace Duetto.Core.FileSystem;

public sealed record FileSystemCapabilities
{
    public required bool CanRename { get; init; }

    public required bool CanCreateEmptyDir { get; init; }

    public required bool CanCreateFile { get; init; }
    public required bool CanDelete { get; init; }

    public required bool HasTrash { get; init; }

    public required bool HasPermissions { get; init; }

    public required bool PreservesMTime { get; init; }

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
