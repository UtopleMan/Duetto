namespace Duet.Core.FileSystem;

public static class DirectoryLister
{
    /// <summary>
    /// Lists a directory including hidden entries. Per-entry metadata failures are
    /// swallowed (entry still returned with best-effort data); an unreadable
    /// directory throws.
    /// </summary>
    public static IReadOnlyList<FileEntry> List(string path)
    {
        var dir = new DirectoryInfo(path);
        var entries = new List<FileEntry>();
        foreach (var info in dir.EnumerateFileSystemInfos())
        {
            try
            {
                entries.Add(ToEntry(info));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
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
