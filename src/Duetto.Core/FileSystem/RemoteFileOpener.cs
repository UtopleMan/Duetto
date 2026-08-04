namespace Duetto.Core.FileSystem;

// Materialises a remote file to a local temp copy so an external app can open it, and owns
// that copy's lifetime. View-only: copies are never uploaded back and are deleted on Dispose.
// Layout: <tempRoot>/<guid>/<originalName> — a per-open guid dir avoids name collisions and
// keeps the real filename (so the OS picks the right app). tempRoot is owned entirely by this
// type, so the startup sweep can safely clear it (recovers files a crashed session left behind).
public sealed class RemoteFileOpener : IDisposable
{
    private readonly FileSystemRegistry _registry;
    private readonly Action<string> _launch;
    private readonly string _tempRoot;
    private readonly List<string> _created = [];
    private readonly object _lock = new();

    public RemoteFileOpener(FileSystemRegistry registry, Action<string> launch, string? tempRoot = null)
    {
        _registry = registry;
        _launch = launch;
        _tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "Duetto", "open");
        Sweep();
    }

    // Copies the remote file to a fresh temp dir and returns the local path.
    public string Download(string fullAddress, CancellationToken ct)
    {
        var (provider, localPath) = _registry.Resolve(fullAddress);
        var name = PathUtil.Leaf(localPath);
        if (string.IsNullOrEmpty(name))
            name = "file";

        var dir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        RestrictToOwner(dir);
        lock (_lock)
            _created.Add(dir);

        var target = Path.Combine(dir, name);
        using (var src = provider.OpenRead(localPath))
        using (var dst = File.Create(target))
            Copy(src, dst, ct);

        return target;
    }

    // Buffered copy that honors cancellation — sync Stream.CopyTo has no CancellationToken overload.
    private static void Copy(Stream src, Stream dst, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = src.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            dst.Write(buffer, 0, read);
        }
    }

    // Downloaded remote files may be sensitive; lock the per-open dir to the owner so a
    // world-readable /tmp (mode 1777 on Linux) cannot leak them to other local users.
    private static void RestrictToOwner(string dir)
    {
        if (OperatingSystem.IsWindows())
            return; // Inherits a private-by-default ACL from the user profile.
        File.SetUnixFileMode(dir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); // 0700
    }

    public void Launch(string path) => _launch(path);

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var dir in _created)
                TryDelete(dir);
            _created.Clear();
        }
    }

    // Best-effort clear of everything under the temp root — recovers leftovers from a
    // previous session that never got to run Dispose (e.g. a crash).
    private void Sweep()
    {
        try
        {
            if (!Directory.Exists(_tempRoot))
                return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(_tempRoot))
                TryDelete(entry);
        }
        catch
        {
            // Sweep is best-effort; never let cleanup abort startup.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A locked or already-removed file is fine — the next startup sweep catches it.
        }
    }
}
