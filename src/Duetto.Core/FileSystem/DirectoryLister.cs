namespace Duetto.Core.FileSystem;

public static class DirectoryLister
{
    public static IReadOnlyList<FileEntry> List(string path)
    {
        var dir = new DirectoryInfo(path);
        var entries = new List<FileEntry>();
        using var iterator = dir.EnumerateFileSystemInfos().GetEnumerator();
        while (true)
        {
            try
            {
                if (!iterator.MoveNext())
                    break;
                entries.Add(ToEntry(iterator.Current));
            }
            catch (IOException)
            {
                break;
            }
            catch (UnauthorizedAccessException)
            {
                break;
            }
        }

        return entries;
    }

    public static FileEntry ToEntry(FileSystemInfo info)
    {
        var isDir = info is DirectoryInfo;
        var size = info is FileInfo file ? file.Length : -1;

        var perms = "";
        var writable = true;
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var mode = info.UnixFileMode;
                perms = FormatUtil.UnixPermissions(mode);
                writable = mode.HasFlag(UnixFileMode.UserWrite);
            }
            catch (IOException)
            {
            }
        }
        else
        {
            writable = !info.Attributes.HasFlag(FileAttributes.ReadOnly);
        }

        return new FileEntry
        {
            Name = info.Name,
            FullPath = info.FullName,
            IsDirectory = isDir,
            SizeBytes = size,
            TypeLabel = FormatUtil.TypeLabel(info.Name, isDir),
            ModifiedUtc = info.LastWriteTimeUtc,
            UnixPermissions = perms,
            AccessSummary = writable ? "RW" : "R",
        };
    }
}
