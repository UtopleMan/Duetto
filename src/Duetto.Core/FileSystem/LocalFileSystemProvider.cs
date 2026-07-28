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

    public string CreateDirectory(string parent, string name)
    {
        var target = Path.Combine(parent, name);
        Directory.CreateDirectory(target);
        return target;
    }

    public string CreateFile(string parent, string name)
    {
        var target = Path.Combine(parent, name);
        File.Create(target).Dispose();
        return target;
    }

    public string Rename(string fullPath, string newName)
    {
        var parent = Path.GetDirectoryName(fullPath)
                     ?? throw new ArgumentException("Cannot rename a root", nameof(fullPath));
        var target = Path.Combine(parent, newName);
        if (Directory.Exists(fullPath))
            Directory.Move(fullPath, target);
        else
            File.Move(fullPath, target);
        return target;
    }

    public void ReplaceFile(string from, string to) => File.Move(from, to, overwrite: true);

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
