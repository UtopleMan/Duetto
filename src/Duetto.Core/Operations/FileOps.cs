using Duetto.Core.FileSystem;

namespace Duetto.Core.Operations;

public static class FileOps
{
    private static readonly IFileSystemProvider Local = new LocalFileSystemProvider();

    public static string Rename(string fullPath, string newName) => Rename(Local, fullPath, newName);

    public static string Rename(IFileSystemProvider provider, string fullPath, string newName)
    {
        RejectSeparators(newName, nameof(newName));
        if (PathUtil.Parent(fullPath) is null)
            throw new ArgumentException("Cannot rename a root", nameof(fullPath));
        return provider.Rename(fullPath, newName);
    }

    public static string NewFolder(string parentDir, string baseName = "New folder") =>
        NewFolder(Local, parentDir, baseName);

    public static string NewFolder(IFileSystemProvider provider, string parentDir, string baseName = "New folder") =>
        CreateFolder(provider, parentDir, SuggestEntryName(provider, parentDir, baseName));

    public static string SuggestEntryName(string parentDir, string baseName) =>
        SuggestEntryName(Local, parentDir, baseName);

    public static string SuggestEntryName(IFileSystemProvider provider, string parentDir, string baseName)
    {
        var name = baseName;
        var n = 1;
        while (Exists(provider, parentDir, name))
            name = $"{baseName} {++n}";
        return name;
    }

    public static string CreateFolder(string parentDir, string name) => CreateFolder(Local, parentDir, name);

    public static string CreateFolder(IFileSystemProvider provider, string parentDir, string name)
    {
        ValidateNewEntry(provider, parentDir, name);
        return provider.CreateDirectory(parentDir, name);
    }

    public static string CreateFile(string parentDir, string name) => CreateFile(Local, parentDir, name);

    public static string CreateFile(IFileSystemProvider provider, string parentDir, string name)
    {
        ValidateNewEntry(provider, parentDir, name);
        return provider.CreateFile(parentDir, name);
    }

    private static bool Exists(IFileSystemProvider provider, string parentDir, string name)
    {
        var target = PathUtil.Combine(parentDir, name);
        return provider.DirectoryExists(target) || provider.FileExists(target);
    }

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
