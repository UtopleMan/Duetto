namespace Duetto.Core.FileSystem;

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

    private static void RestrictToOwner(string dir)
    {
        if (OperatingSystem.IsWindows())
            return;
        File.SetUnixFileMode(dir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
        }
    }
}
