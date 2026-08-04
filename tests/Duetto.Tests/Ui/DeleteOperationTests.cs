using Avalonia.Headless.XUnit;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Renci.SshNet.Common;
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
    public async Task Delete_whenAccessDenied_reportsFailure_notFalseSuccess()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = _ => throw new UnauthorizedAccessException("denied");

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion;

        var op = (SimpleOperationViewModel)vm.ActiveOperation!;
        Assert.True(op.IsFinished);
        Assert.Contains("Couldn't delete", op.Title);
        Assert.DoesNotContain("Moved 1", op.Title);
        Assert.DoesNotContain("Deleted 1", op.Title);
    }

    [AvaloniaFact]
    public async Task Delete_whenRemoteThrowsSshException_isReported_withoutFaulting()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        // SFTP permission-denied surfaces as an SshException — it must be reported, not fault the task.
        vm.TrashFn = _ => throw new SshException("permission denied");

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion; // must not throw

        var op = (SimpleOperationViewModel)vm.ActiveOperation!;
        Assert.True(op.IsFinished);
        Assert.Contains("Couldn't delete", op.Title);
    }

    [AvaloniaFact]
    public async Task Delete_whenSomeItemsFail_reportsPartialCounts()
    {
        using var tmp = new TempDir();
        tmp.File("ok.txt", "a");
        tmp.File("bad.txt", "b");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        var trashed = new List<string>();
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = p =>
        {
            if (p.EndsWith("bad.txt", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("denied");
            trashed.Add(p);
            return null;
        };

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion;

        Assert.Single(trashed);
        Assert.Contains("ok.txt", trashed[0]);
        Assert.Contains("failed", ((SimpleOperationViewModel)vm.ActiveOperation!).Title);
    }

    [AvaloniaFact]
    public async Task Delete_success_dismissesBriefly()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = _ => null;

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion;

        Assert.Equal(1.0, ((SimpleOperationViewModel)vm.ActiveOperation!).DismissAfterSeconds);
    }

    [AvaloniaFact]
    public async Task Delete_failure_lingersFiveSeconds_soTheUserCanReadIt()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = _ => throw new UnauthorizedAccessException("denied");

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);
        await vm.DeleteCompletion;

        Assert.Equal(5.0, ((SimpleOperationViewModel)vm.ActiveOperation!).DismissAfterSeconds);
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
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = p => { trashed.Add(p); return null; };

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal(3, trashed.Count);
        Assert.True(vm.ActiveOperation!.IsFinished);
    }

    [AvaloniaFact]
    public void Delete_withNothingMarked_isNoOp()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        tmp.File("b.txt", "b");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        var trashed = new List<string>();
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = p => { trashed.Add(p); return null; };

        // The cursor sits on a row, but nothing is marked — delete must touch nothing.
        vm.Left.SelectByName("b.txt");
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Empty(trashed);
        Assert.Null(vm.ActiveOperation);
    }

    [AvaloniaFact]
    public void Delete_movesCursorToNeighbor_soTheListStaysFocusable()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        tmp.File("b.txt", "b");
        tmp.File("c.txt", "c");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        vm.DeleteScheduler = (work, ct) => { work(ct); return Task.CompletedTask; };
        vm.TrashFn = p => { File.Delete(p); return null; };

        vm.Left.SelectByName("b.txt");
        vm.Left.ToggleMarkAt(vm.Left.CursorRow!);
        vm.DeleteSelectedCommand.Execute(null);

        // b.txt is gone; the cursor must land on a real row (the one that took its slot),
        // not vanish — an empty selection leaves no container to keep keyboard focus.
        Assert.DoesNotContain(vm.Left.Rows, r => r.Name == "b.txt");
        var cursor = vm.Left.Selection.SelectedItem;
        Assert.NotNull(cursor);
        Assert.False(cursor!.IsParentNav);
        Assert.Equal("c.txt", cursor.Name);
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
            if (trashed.Count == 1)
                ((SimpleOperationViewModel)vm.ActiveOperation!).CancelOrDismissCommand.Execute(null);
            return null;
        };

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Single(trashed);
        Assert.Null(vm.ActiveOperation);
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

        Assert.Equal(2, trashed.Count);
    }

    [AvaloniaFact]
    public void SecondOperation_isBlocked_whileTheSlotIsBusy()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        // A delete whose worker never completes keeps the strip slot occupied.
        vm.DeleteScheduler = (_, _) => new TaskCompletionSource<bool>().Task;
        vm.TrashFn = _ => null;

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);
        var firstOp = vm.ActiveOperation;
        Assert.NotNull(firstOp);

        MarkAll(vm.Left);
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Same(firstOp, vm.ActiveOperation);
    }
}
