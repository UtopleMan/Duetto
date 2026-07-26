namespace Duet.Core.Operations;

public static class FileOps
{
    /// <summary>Renames a file or directory in place. Returns the new full path.</summary>
    public static string Rename(string fullPath, string newName)
    {
        if (newName.Contains(Path.DirectorySeparatorChar) || newName.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Name cannot contain path separators", nameof(newName));

        var parent = Path.GetDirectoryName(fullPath)
                     ?? throw new ArgumentException("Cannot rename a root", nameof(fullPath));
        var target = Path.Combine(parent, newName);
        if (Directory.Exists(fullPath))
            Directory.Move(fullPath, target);
        else
            File.Move(fullPath, target);
        return target;
    }

    /// <summary>Creates "New folder" (or "New folder 2", …) inside <paramref name="parentDir"/>.</summary>
    public static string NewFolder(string parentDir, string baseName = "New folder")
    {
        var name = baseName;
        var n = 1;
        while (Directory.Exists(Path.Combine(parentDir, name)) || File.Exists(Path.Combine(parentDir, name)))
            name = $"{baseName} {++n}";
        var path = Path.Combine(parentDir, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
