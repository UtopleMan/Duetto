using Duetto.Core.Operations;

namespace Duetto.Core.FileSystem;

/// <summary>
/// The local disk as an <see cref="IFileSystemProvider"/> — a thin adapter over the
/// existing <see cref="DirectoryLister"/>, <see cref="FileOps"/>, <see cref="TrashService"/>
/// and <see cref="VolumeCatalog"/>, so the local path keeps behaving exactly as before.
/// </summary>
public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    public FileSystemCapabilities Capabilities => FileSystemCapabilities.LocalDisk;

    public IReadOnlyList<FileEntry> List(string path) => DirectoryLister.List(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);

    public FileEntry? Stat(string path)
    {
        if (Directory.Exists(path))
            return DirectoryLister.ToEntry(new DirectoryInfo(path));
        if (File.Exists(path))
            return DirectoryLister.ToEntry(new FileInfo(path));
        return null;
    }

    public string CreateDirectory(string parent, string name) => FileOps.CreateFolder(parent, name);
    public string CreateFile(string parent, string name) => FileOps.CreateFile(parent, name);
    public string Rename(string fullPath, string newName) => FileOps.Rename(fullPath, newName);

    public void Delete(string path, bool toTrash)
    {
        if (toTrash)
        {
            TrashService.Trash(path);
            return;
        }

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);
    }

    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream OpenWrite(string path) => File.Create(path);
    public void SetLastWriteTimeUtc(string path, DateTime utc) => File.SetLastWriteTimeUtc(path, utc);

    public IEnumerable<FileEntry> EnumerateRecursive(string path)
    {
        IEnumerable<FileSystemInfo> children;
        try
        {
            children = new DirectoryInfo(path).EnumerateFileSystemInfos();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var info in children)
        {
            yield return DirectoryLister.ToEntry(info);
            if (info is DirectoryInfo dir)
                foreach (var descendant in EnumerateRecursive(dir.FullName))
                    yield return descendant;
        }
    }

    public VolumeInfo? VolumeFor(string path) => VolumeCatalog.FindByPath(VolumeCatalog.List(), path);
}
