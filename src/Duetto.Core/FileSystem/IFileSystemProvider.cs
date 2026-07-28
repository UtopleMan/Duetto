namespace Duetto.Core.FileSystem;

/// <summary>
/// A backend that lists and mutates entries under some address space (the local disk,
/// an SFTP host, later S3/SMB). Paths passed in are provider-local (already stripped of
/// any <c>scheme://id/</c> prefix by <see cref="FileSystemRegistry"/>). Optional
/// operations throw <see cref="NotSupportedException"/> when the matching capability is
/// off; callers should check <see cref="Capabilities"/> first.
/// </summary>
public interface IFileSystemProvider
{
    FileSystemCapabilities Capabilities { get; }

    IReadOnlyList<FileEntry> List(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);

    /// <summary>Metadata for a single entry, or null when it does not exist.</summary>
    FileEntry? Stat(string path);

    /// <summary>Creates a directory named <paramref name="name"/> under <paramref name="parent"/>; returns its full path.</summary>
    string CreateDirectory(string parent, string name);

    /// <summary>Creates an empty file named <paramref name="name"/> under <paramref name="parent"/>; returns its full path.</summary>
    string CreateFile(string parent, string name);

    /// <summary>Renames the leaf of <paramref name="fullPath"/> to <paramref name="newName"/>; returns the new full path.</summary>
    string Rename(string fullPath, string newName);

    /// <summary>
    /// Moves the file at <paramref name="from"/> onto <paramref name="to"/>, replacing any
    /// existing file there — atomically when the backend supports it (a same-directory
    /// POSIX rename). Used to finish a ".part" transfer without a visible gap at the target.
    /// </summary>
    void ReplaceFile(string from, string to);

    /// <summary>
    /// Removes an entry (recursively for directories). <paramref name="toTrash"/> is honored
    /// only when <see cref="FileSystemCapabilities.HasTrash"/>; otherwise it is a permanent delete.
    /// </summary>
    void Delete(string path, bool toTrash);

    Stream OpenRead(string path);

    /// <summary>Opens a writable stream, creating or truncating the target.</summary>
    Stream OpenWrite(string path);

    void SetLastWriteTimeUtc(string path, DateTime utc);

    /// <summary>Depth-first walk of every file and directory under <paramref name="path"/>.</summary>
    IEnumerable<FileEntry> EnumerateRecursive(string path);

    /// <summary>Capacity/label for the volume containing <paramref name="path"/>, or null when unknown.</summary>
    VolumeInfo? VolumeFor(string path);
}
