using Avalonia.Headless.XUnit;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Xunit;

namespace Duetto.Tests.Ui;

public class DeleteOperationTests
{
    private static void MarkAll(PaneViewModel pane)
    {
        foreach (var row in pane.Rows.Where(r => !r.IsParentNav).ToList())
            pane.ToggleMarkAt(row);
    }

    [AvaloniaFact]
    public void Delete_trashesEveryMarkedItem_thenFinishes()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        tmp.File("b.txt", "b");
        tmp.File("c.txt", "c");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        var trashed = new List<string>();
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; }; // inline
        vm.TrashFn = p => { trashed.Add(p); return null; };

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal(3, trashed.Count);
        Assert.True(vm.ActiveOperation!.IsFinished);
    }

    [AvaloniaFact]
    public void Delete_cancel_stopsBeforeNextItem()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        tmp.File("b.txt", "b");
        tmp.File("c.txt", "c");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        var trashed = new List<string>();
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = p =>
        {
            trashed.Add(p);
            if (trashed.Count == 1) // cancel right after the first item
                ((SimpleOperationViewModel)vm.ActiveOperation!).CancelOrDismissCommand.Execute(null);
            return null;
        };

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Single(trashed);            // stopped before the 2nd item
        Assert.Null(vm.ActiveOperation);   // cancel dismissed the strip
    }

    [AvaloniaFact]
    public void Delete_perItemFailure_isSwallowed_batchContinues()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        tmp.File("b.txt", "b");
        tmp.File("c.txt", "c");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        var trashed = new List<string>();
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = p =>
        {
            if (p.EndsWith("b.txt"))
                throw new IOException("boom");
            trashed.Add(p);
            return null;
        };

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal(2, trashed.Count); // a + c trashed; b failed but did not abort the batch
    }
}
