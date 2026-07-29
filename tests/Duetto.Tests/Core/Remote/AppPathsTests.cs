using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Verifies that AppPaths returns non-empty, OS-appropriate paths and that
/// accessing ConfigDir creates the directory (idempotent).
/// These tests compute paths but do NOT write outside a TempDir.
/// </summary>
public class AppPathsTests
{
    [Fact]
    public void ConfigDir_is_not_empty()
    {
        var dir = AppPaths.ConfigDir;
        Assert.False(string.IsNullOrWhiteSpace(dir));
    }

    [Fact]
    public void ConfigDir_ends_with_Duetto_or_duetto()
    {
        var dir = AppPaths.ConfigDir;
        var last = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Assert.True(
            last.Equals("Duetto", StringComparison.OrdinalIgnoreCase),
            $"Expected last path segment to be 'Duetto' (case-insensitive), got '{last}'");
    }

    [Fact]
    public void ConfigDir_creates_directory_idempotently()
    {
        // Calling twice must not throw.
        var dir1 = AppPaths.ConfigDir;
        var dir2 = AppPaths.ConfigDir;
        Assert.Equal(dir1, dir2);
        Assert.True(Directory.Exists(dir1));
    }

    [Fact]
    public void ConnectionsJsonPath_is_inside_ConfigDir()
    {
        var dir = AppPaths.ConfigDir;
        var path = AppPaths.ConnectionsJsonPath;
        Assert.StartsWith(dir, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("connections.json", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostKeysJsonPath_is_inside_ConfigDir()
    {
        var dir = AppPaths.ConfigDir;
        var path = AppPaths.HostKeysJsonPath;
        Assert.StartsWith(dir, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("hostkeys.json", path, StringComparison.OrdinalIgnoreCase);
    }
}
