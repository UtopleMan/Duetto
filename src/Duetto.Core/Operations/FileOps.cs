namespace Duetto.Core.Operations;

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
    public static string NewFolder(string parentDir, string baseName = "New folder") =>
        CreateFolder(parentDir, SuggestEntryName(parentDir, baseName));

    /// <summary>
    /// First free entry name inside <paramref name="parentDir"/> based on
    /// <paramref name="baseName"/> ("New folder", "New folder 2", …). Checks both files
    /// and directories; does not create anything.
    /// </summary>
    public static string SuggestEntryName(string parentDir, string baseName)
    {
        var name = baseName;
        var n = 1;
        while (Directory.Exists(Path.Combine(parentDir, name)) || File.Exists(Path.Combine(parentDir, name)))
            name = $"{baseName} {++n}";
        return name;
    }

    /// <summary>Creates a directory named exactly <paramref name="name"/>. Returns the full path.</summary>
    public static string CreateFolder(string parentDir, string name)
    {
        var target = ValidateNewEntry(parentDir, name);
        Directory.CreateDirectory(target);
        return target;
    }

    /// <summary>Creates an empty file named exactly <paramref name="name"/>. Returns the full path.</summary>
    public static string CreateFile(string parentDir, string name)
    {
        var target = ValidateNewEntry(parentDir, name);
        File.Create(target).Dispose();
        return target;
    }

    /// <summary>
    /// Guards a to-be-created entry: rejects path separators and refuses to clobber an
    /// existing file or directory. Returns the validated full target path.
    /// </summary>
    private static string ValidateNewEntry(string parentDir, string name)
    {
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Name cannot contain path separators", nameof(name));

        var target = Path.Combine(parentDir, name);
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"\"{name}\" already exists");
        return target;
    }
}
