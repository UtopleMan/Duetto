using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class DragDropTests
{
    private static async Task WaitForCompletion(MainViewModel vm)
    {
        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;
        vm.ActiveTransfer.UpdateNow();
    }

    [AvaloniaFact]
    public async Task Drop_between_panes_copies_by_default()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        src.File("one.txt", "111");
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("one.txt");

        vm.DropBetweenPanes(vm.Left, vm.Right, moveRequested: false);
        await WaitForCompletion(vm);

        Assert.True(File.Exists(Path.Combine(dst.Path, "one.txt")));
        Assert.True(File.Exists(Path.Combine(src.Path, "one.txt")));
    }

    [AvaloniaFact]
    public async Task Drop_between_panes_with_shift_moves()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        src.File("mover.txt", "x");
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("mover.txt");

        vm.DropBetweenPanes(vm.Left, vm.Right, moveRequested: true);
        await WaitForCompletion(vm);
        vm.ActiveTransfer!.Dismiss();

        Assert.False(File.Exists(Path.Combine(src.Path, "mover.txt")));
        Assert.True(File.Exists(Path.Combine(dst.Path, "mover.txt")));
    }

    [AvaloniaFact]
    public void Drop_onto_same_pane_is_ignored()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        src.File("stay.txt", "x");
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("stay.txt");

        vm.DropBetweenPanes(vm.Left, vm.Left, moveRequested: false);

        Assert.Null(vm.ActiveTransfer);
    }

    [AvaloniaFact]
    public async Task Drop_from_os_copies_into_local_pane()
    {
        using var os = new TempDir();
        using var dst = new TempDir();
        var file1 = os.File("a.txt", "aaa");
        var file2 = os.File("b.txt", "bbb");
        using var vm = new MainViewModel(dst.Path, dst.Path);

        vm.DropFromOs(vm.Right, [file1, file2], moveRequested: false);
        await WaitForCompletion(vm);

        Assert.True(File.Exists(Path.Combine(dst.Path, "a.txt")));
        Assert.True(File.Exists(Path.Combine(dst.Path, "b.txt")));
        Assert.True(File.Exists(file1));
        Assert.True(File.Exists(file2));
    }

    [AvaloniaFact]
    public async Task Drop_from_os_with_shift_moves()
    {
        using var os = new TempDir();
        using var dst = new TempDir();
        var file1 = os.File("gone.txt", "x");
        using var vm = new MainViewModel(dst.Path, dst.Path);

        vm.DropFromOs(vm.Right, [file1], moveRequested: true);
        await WaitForCompletion(vm);
        vm.ActiveTransfer!.Dismiss();

        Assert.False(File.Exists(file1));
        Assert.True(File.Exists(Path.Combine(dst.Path, "gone.txt")));
    }

    [AvaloniaFact]
    public async Task Drop_from_os_uploads_into_remote_pane()
    {
        using var os = new TempDir();
        var file1 = os.File("upload.txt", "payload");

        var remoteFs = new InMemoryFileSystemProvider();
        remoteFs.CreateDirectory("/", "incoming");
        var reg = new FileSystemRegistry();
        reg.Register("fake", "host", remoteFs);

        using var vm = new MainViewModel(os.Path, "fake://host/incoming", registry: reg);
        await vm.Right.LoadCompletion;

        vm.DropFromOs(vm.Right, [file1], moveRequested: false);
        await WaitForCompletion(vm);

        Assert.True(remoteFs.FileExists("/incoming/upload.txt"));
    }

    [AvaloniaFact]
    public void Local_drag_payload_returns_selection_for_local_pane()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        var file1 = src.File("a.txt", "aaa");
        var file2 = src.File("b.txt", "bbb");
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("a.txt");
        vm.Left.ToggleMarkAt(vm.Left.CursorRow!);
        vm.Left.SelectByName("b.txt");
        vm.Left.ToggleMarkAt(vm.Left.CursorRow!);

        var payload = vm.LocalDragPayload(vm.Left);

        Assert.NotNull(payload);
        Assert.Equal([file1, file2], payload!.OrderBy(p => p).ToList());
    }

    [AvaloniaFact]
    public async Task Local_drag_payload_is_null_for_remote_pane()
    {
        using var dst = new TempDir();
        var remoteFs = new InMemoryFileSystemProvider();
        remoteFs.CreateDirectory("/", "dir");
        var reg = new FileSystemRegistry();
        reg.Register("fake", "host", remoteFs);

        using var vm = new MainViewModel("fake://host/dir", dst.Path, registry: reg);
        await vm.Left.LoadCompletion;

        Assert.Null(vm.LocalDragPayload(vm.Left));
    }
}
