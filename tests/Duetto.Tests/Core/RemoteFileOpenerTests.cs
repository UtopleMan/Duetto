using System.Text;
using Duetto.Core.FileSystem;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

public class RemoteFileOpenerTests
{
    private static (FileSystemRegistry Reg, InMemoryFileSystemProvider Fs) RemoteRegistry()
    {
        var fs = new InMemoryFileSystemProvider();
        var reg = new FileSystemRegistry();
        reg.Register("sftp", "srv", fs);
        return (reg, fs);
    }

    private static void Seed(InMemoryFileSystemProvider fs, string parent, string name, string content)
    {
        var full = fs.CreateFile(parent, name);
        using var w = fs.OpenWrite(full);
        w.Write(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public void Download_copies_bytes_to_temp_file_named_after_source()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "hello remote");

        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);

        Assert.Equal("note.txt", Path.GetFileName(path));
        Assert.Equal("hello remote", File.ReadAllText(path));
    }

    [Fact]
    public void Download_places_file_under_temp_root()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);

        Assert.StartsWith(tmp.Path, path);
    }

    [Fact]
    public void Launch_invokes_the_injected_launcher_with_the_temp_path()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        string? launched = null;
        using var opener = new RemoteFileOpener(reg, p => launched = p, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);
        opener.Launch(path);

        Assert.Equal(path, launched);
    }

    [Fact]
    public void Dispose_deletes_downloaded_files()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);
        Assert.True(File.Exists(path));

        opener.Dispose();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Constructor_sweeps_preexisting_temp_root_contents()
    {
        using var tmp = new TempDir();
        var leftover = Path.Combine(tmp.Path, "stale");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "old.txt"), "junk");

        var (reg, _) = RemoteRegistry();
        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);

        Assert.False(Directory.Exists(leftover));
    }

    [Fact]
    public void Cancelled_token_aborts_download_and_does_not_launch()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "hello remote");

        var launched = false;
        using var opener = new RemoteFileOpener(reg, _ => launched = true, tmp.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => opener.Download("sftp://srv/note.txt", cts.Token));
        Assert.False(launched);
    }

    [Fact]
    public void Download_dir_is_owner_only_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes are a no-op on Windows.

        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        using var opener = new RemoteFileOpener(reg, _ => { }, tmp.Path);
        var path = opener.Download("sftp://srv/note.txt", CancellationToken.None);

        var mode = File.GetUnixFileMode(Path.GetDirectoryName(path)!);
        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(expected, mode);
    }
}
