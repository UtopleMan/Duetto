using Duetto.Core.Operations;

namespace Duetto.Core.FileSystem;

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

    public void Move(string fromPath, string toPath)
    {
        if (Directory.Exists(fromPath))
            Directory.Move(fromPath, toPath);
        else
            File.Move(fromPath, toPath);
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
        // Guard every MoveNext: macOS TCC can deny individual entries mid-iteration.
        IEnumerator<FileSystemInfo>? iterator;
        try
        {
            iterator = new DirectoryInfo(path).EnumerateFileSystemInfos().GetEnumerator();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        using (iterator)
        {
            while (true)
            {
                FileSystemInfo info;
                try
                {
                    if (!iterator.MoveNext())
                        break;
                    info = iterator.Current;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                yield return DirectoryLister.ToEntry(info);
                // Recurse into real directories only; symlinked dirs risk cycles.
                if (info is DirectoryInfo dir && dir.LinkTarget is null)
                    foreach (var descendant in EnumerateRecursive(dir.FullName))
                        yield return descendant;
            }
        }
    }

    public VolumeInfo? VolumeFor(string path) => VolumeCatalog.FindByPath(VolumeCatalog.List(), path);
}
