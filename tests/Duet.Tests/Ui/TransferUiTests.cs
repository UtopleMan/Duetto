using Avalonia.Headless.XUnit;
using Duet.Core.Operations;
using Duet.Tests.Core;
using Duet.ViewModels;

namespace Duet.Tests.Ui;

public class TransferUiTests
{
    private static async Task WaitForCompletion(MainViewModel vm)
    {
        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;
        vm.ActiveTransfer.UpdateNow();
    }

    [AvaloniaFact]
    public async Task Copy_selected_copies_to_other_pane_and_updates_strip()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        src.File("one.txt", "111");
        src.File("two.txt", "222");
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("one.txt");

        vm.CopySelected();
        await WaitForCompletion(vm);

        Assert.True(File.Exists(Path.Combine(dst.Path, "one.txt")));
        Assert.False(File.Exists(Path.Combine(dst.Path, "two.txt")));
        var t = vm.ActiveTransfer!;
        Assert.True(t.IsFinished);
        Assert.Equal("1 of 1 files done", t.FilesLine);
        Assert.Equal(100, t.DonePercent, 1);

        var row = vm.Left.Rows.Single(r => r.Name == "one.txt");
        Assert.Equal("done", row.TransferStatus);
    }

    [AvaloniaFact]
    public async Task Conflict_is_skipped_and_reviewable()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        src.File("clash.txt", "old", DateTime.UtcNow.AddDays(-1));
        dst.File("clash.txt", "newer", DateTime.UtcNow);
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("clash.txt");

        vm.CopySelected();
        await WaitForCompletion(vm);

        var t = vm.ActiveTransfer!;
        Assert.True(t.HasSkipped);
        Assert.Contains("1 skipped", t.SkippedLine);
        Assert.Contains(TransferEngine.SkipReasonNewer, t.SkippedLine);
        Assert.Equal(["clash.txt"], t.SkippedItems);
        Assert.Equal("newer", File.ReadAllText(Path.Combine(dst.Path, "clash.txt")));
    }

    [AvaloniaFact]
    public async Task Move_selected_moves_and_dismiss_reloads_panes()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        src.File("mover.txt", "x");
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("mover.txt");

        vm.MoveSelected();
        await WaitForCompletion(vm);
        vm.ActiveTransfer!.Dismiss();

        Assert.Null(vm.ActiveTransfer);
        Assert.DoesNotContain(vm.Left.Rows, r => r.Name == "mover.txt");
        Assert.Contains(vm.Right.Rows, r => r.Name == "mover.txt");
    }

    [AvaloniaFact]
    public async Task Cancel_mid_copy_stops_transfer()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        src.File("big.bin", new string('x', 30 * 1024 * 1024));
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("big.bin");

        vm.CopySelected();
        var transfer = vm.ActiveTransfer!;
        transfer.Session.Cancel();
        await transfer.Session.Completion;

        Assert.True(transfer.Session.Snapshot().IsCancelled);
        Assert.Empty(Directory.EnumerateFiles(dst.Path, "*.part"));
    }

    [AvaloniaFact]
    public void Delete_selected_sends_to_trash()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        var doomed = src.File("doomed.txt", "x");
        using var vm = new MainViewModel(src.Path, dst.Path);
        vm.Left.SelectByName("doomed.txt");

        vm.DeleteSelected();

        Assert.False(File.Exists(doomed));
        Assert.DoesNotContain(vm.Left.Rows, r => r.Name == "doomed.txt");
    }
}
