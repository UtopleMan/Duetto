namespace Duetto.Core.FileSystem;

// Paths passed in are provider-local (already stripped of any scheme://id/ prefix by
// FileSystemRegistry). Optional operations throw NotSupportedException when the matching
// capability is off; callers should check Capabilities first.
public interface IFileSystemProvider
{
    FileSystemCapabilities Capabilities { get; }

    IReadOnlyList<FileEntry> List(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);

    // Null when the entry does not exist.
    FileEntry? Stat(string path);

    string CreateDirectory(string parent, string name);

    string CreateFile(string parent, string name);

    string Rename(string fullPath, string newName);

    // Atomic where the backend supports it (e.g. same-filesystem POSIX rename). Throws
    // IOException when the destination already exists; NotSupportedException when off.
    void Move(string fromPath, string toPath);

    // Replaces any existing file at the target — atomically when the backend supports it (a
    // same-directory POSIX rename). Used to finish a ".part" transfer without a visible gap.
    void ReplaceFile(string from, string to);

    // Recursive for directories. toTrash is honored only when Capabilities.HasTrash;
    // otherwise it is a permanent delete.
    void Delete(string path, bool toTrash);

    Stream OpenRead(string path);

    // Creates or truncates the target.
    Stream OpenWrite(string path);

    void SetLastWriteTimeUtc(string path, DateTime utc);

    IEnumerable<FileEntry> EnumerateRecursive(string path);

    // Null when the volume is unknown.
    VolumeInfo? VolumeFor(string path);
}
