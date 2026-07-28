using Duetto.Core.FileSystem;

namespace Duetto.Core.Operations;

public static class FileOps
{
    /// <summary>The default backend used by the provider-less overloads (the local disk).</summary>
    private static readonly IFileSystemProvider Local = new LocalFileSystemProvider();

    /// <summary>Renames a file or directory in place. Returns the new full path.</summary>
    public static string Rename(string fullPath, string newName) => Rename(Local, fullPath, newName);

    /// <summary>Renames the leaf of <paramref name="fullPath"/> through <paramref name="provider"/>. Returns the new full path.</summary>
    public static string Rename(IFileSystemProvider provider, string fullPath, string newName)
    {
        RejectSeparators(newName, nameof(newName));
        if (PathUtil.Parent(fullPath) is null)
            throw new ArgumentException("Cannot rename a root", nameof(fullPath));
        return provider.Rename(fullPath, newName);
    }

    /// <summary>Creates "New folder" (or "New folder 2", …) inside <paramref name="parentDir"/>.</summary>
    public static string NewFolder(string parentDir, string baseName = "New folder") =>
        NewFolder(Local, parentDir, baseName);

    /// <summary>Creates "New folder" (or "New folder 2", …) inside <paramref name="parentDir"/> on <paramref name="provider"/>.</summary>
    public static string NewFolder(IFileSystemProvider provider, string parentDir, string baseName = "New folder") =>
        CreateFolder(provider, parentDir, SuggestEntryName(provider, parentDir, baseName));

    /// <summary>
    /// First free entry name inside <paramref name="parentDir"/> based on
    /// <paramref name="baseName"/> ("New folder", "New folder 2", …). Checks both files
    /// and directories; does not create anything.
    /// </summary>
    public static string SuggestEntryName(string parentDir, string baseName) =>
        SuggestEntryName(Local, parentDir, baseName);

    /// <summary>First free entry name inside <paramref name="parentDir"/> on <paramref name="provider"/>; creates nothing.</summary>
    public static string SuggestEntryName(IFileSystemProvider provider, string parentDir, string baseName)
    {
        var name = baseName;
        var n = 1;
        while (Exists(provider, parentDir, name))
            name = $"{baseName} {++n}";
        return name;
    }

    /// <summary>Creates a directory named exactly <paramref name="name"/>. Returns the full path.</summary>
    public static string CreateFolder(string parentDir, string name) => CreateFolder(Local, parentDir, name);

    /// <summary>Creates a directory named exactly <paramref name="name"/> on <paramref name="provider"/>. Returns the full path.</summary>
    public static string CreateFolder(IFileSystemProvider provider, string parentDir, string name)
    {
        ValidateNewEntry(provider, parentDir, name);
        return provider.CreateDirectory(parentDir, name);
    }

    /// <summary>Creates an empty file named exactly <paramref name="name"/>. Returns the full path.</summary>
    public static string CreateFile(string parentDir, string name) => CreateFile(Local, parentDir, name);

    /// <summary>Creates an empty file named exactly <paramref name="name"/> on <paramref name="provider"/>. Returns the full path.</summary>
    public static string CreateFile(IFileSystemProvider provider, string parentDir, string name)
    {
        ValidateNewEntry(provider, parentDir, name);
        return provider.CreateFile(parentDir, name);
    }

    /// <summary>True when a file or directory named <paramref name="name"/> already exists under <paramref name="parentDir"/>.</summary>
    private static bool Exists(IFileSystemProvider provider, string parentDir, string name)
    {
        var target = PathUtil.Combine(parentDir, name);
        return provider.DirectoryExists(target) || provider.FileExists(target);
    }

    /// <summary>
    /// Guards a to-be-created entry: rejects path separators and refuses to clobber an
    /// existing file or directory.
    /// </summary>
    private static void ValidateNewEntry(IFileSystemProvider provider, string parentDir, string name)
    {
        RejectSeparators(name, nameof(name));
        if (Exists(provider, parentDir, name))
            throw new IOException($"\"{name}\" already exists");
    }

    private static void RejectSeparators(string name, string paramName)
    {
        if (name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("Name cannot contain path separators", paramName);
    }
}
