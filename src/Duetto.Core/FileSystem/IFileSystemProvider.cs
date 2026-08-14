namespace Duetto.Core.FileSystem;

public interface IFileSystemProvider
{
    FileSystemCapabilities Capabilities { get; }

    IReadOnlyList<FileEntry> List(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);

    FileEntry? Stat(string path);

    string CreateDirectory(string parent, string name);

    string CreateFile(string parent, string name);

    string Rename(string fullPath, string newName);

    void Move(string fromPath, string toPath);

    void ReplaceFile(string from, string to);

    void Delete(string path, bool toTrash);

    Stream OpenRead(string path);

    Stream OpenWrite(string path);

    void SetLastWriteTimeUtc(string path, DateTime utc);

    IEnumerable<FileEntry> EnumerateRecursive(string path);

    VolumeInfo? VolumeFor(string path);
}
