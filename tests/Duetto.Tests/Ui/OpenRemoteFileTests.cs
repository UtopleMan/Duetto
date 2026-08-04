using System.Text;
using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class OpenRemoteFileTests
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

    [AvaloniaFact]
    public async Task Open_remote_file_downloads_to_temp_and_launches_it()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "hello remote");

        using var vm = new MainViewModel(
            "sftp://srv/", "sftp://srv/", registry: reg, remoteOpenTempRoot: tmp.Path);
        await vm.Left.LoadCompletion;

        string? launched = null;
        vm.Left.LaunchFile = p => launched = p;
        vm.OpenScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };

        vm.Left.SelectByName("note.txt");
        vm.Left.Open(vm.Left.CursorRow!);
        await vm.OpenCompletion;

        Assert.NotNull(launched);
        Assert.Equal("note.txt", Path.GetFileName(launched!));
        Assert.Equal("hello remote", File.ReadAllText(launched!));
        Assert.StartsWith(tmp.Path, launched!);
    }

    [AvaloniaFact]
    public async Task Open_remote_file_is_noop_while_an_operation_is_running()
    {
        using var tmp = new TempDir();
        var (reg, fs) = RemoteRegistry();
        Seed(fs, "/", "note.txt", "x");

        using var vm = new MainViewModel(
            "sftp://srv/", "sftp://srv/", registry: reg, remoteOpenTempRoot: tmp.Path);
        await vm.Left.LoadCompletion;

        var launchCount = 0;
        vm.Left.LaunchFile = _ => launchCount++;
        // First open never completes — leaves ActiveOperation unfinished.
        var gate = new TaskCompletionSource();
        vm.OpenScheduler = (_, _) => gate.Task;

        vm.Left.SelectByName("note.txt");
        vm.Left.Open(vm.Left.CursorRow!);
        var first = vm.ActiveOperation;
        Assert.NotNull(first);
        Assert.False(first!.IsFinished);

        // Second open must be ignored while the first is in flight.
        vm.Left.Open(vm.Left.CursorRow!);

        Assert.Same(first, vm.ActiveOperation);
        Assert.Equal(0, launchCount);
    }
}
