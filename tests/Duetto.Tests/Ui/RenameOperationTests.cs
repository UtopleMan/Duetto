using Avalonia.Headless.XUnit;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Xunit;

namespace Duetto.Tests.Ui;

public class RenameOperationTests
{
    [AvaloniaFact]
    public async Task Rename_runsViaScheduler_thenSelectsRenamedRow()
    {
        using var tmp = new TempDir();
        tmp.File("old.txt", "x");
        using var vm = new PaneViewModel(tmp.Path);
        vm.SelectByName("old.txt");

        var gate = new TaskCompletionSource();
        vm.RenameScheduler = async work => { await gate.Task; work(); };

        var row = vm.StartRename()!;
        row.EditName = "new.txt";
        vm.CommitRename(row);

        // Scheduler is gated: the move has not happened yet.
        Assert.True(File.Exists(Path.Combine(tmp.Path, "old.txt")));

        gate.SetResult();
        await vm.RenameCompletion;

        Assert.True(File.Exists(Path.Combine(tmp.Path, "new.txt")));
        Assert.False(File.Exists(Path.Combine(tmp.Path, "old.txt")));
        Assert.Equal("new.txt", vm.CursorRow?.Name);
    }
}
