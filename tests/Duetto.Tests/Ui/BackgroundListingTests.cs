using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Xunit;

namespace Duetto.Tests.Ui;

public class BackgroundListingTests
{
    // Hand-controlled load scheduler: nothing runs until the test releases it.
    private sealed class ManualScheduler
    {
        private readonly List<(Func<IReadOnlyList<FileEntry>> Work, CancellationToken Ct,
            TaskCompletionSource<IReadOnlyList<FileEntry>> Tcs)> _pending = [];

        public int Count => _pending.Count;

        public Task<IReadOnlyList<FileEntry>> Schedule(Func<IReadOnlyList<FileEntry>> work, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<IReadOnlyList<FileEntry>>();
            _pending.Add((work, ct, tcs));
            return tcs.Task;
        }

        public void Release(int index)
        {
            var (work, ct, tcs) = _pending[index];
            if (ct.IsCancellationRequested)
                tcs.SetCanceled(ct);
            else
                tcs.SetResult(work());
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void Listing_isLoading_whileWorkerPending_thenRowsAppear()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "a");
        tmp.File("b.txt", "b");
        using var vm = new PaneViewModel(tmp.Path); // initial load is synchronous (default scheduler)

        var ms = new ManualScheduler();
        vm.LoadScheduler = ms.Schedule;
        vm.Reload(preserveSelection: false);

        Assert.True(vm.IsLoading);
        Assert.Equal(1, ms.Count);

        ms.Release(0);

        Assert.False(vm.IsLoading);
        Assert.Equal(["..", "a.txt", "b.txt"], vm.Rows.Select(r => r.Name));
    }

    // A Lister that throws HostKeyChangedException must not leave IsLoading stuck: the work-lambda
    // catch must absorb it, return [], and let ApplyWhenReady reach IsLoading = false normally.
    [AvaloniaFact]
    public void HostKeyChanged_during_reload_does_not_stick_IsLoading()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path); // initial load is synchronous

        vm.Lister = _ => throw new HostKeyChangedException(
            "srv", "old-fp", "new-fp", "ssh-ed25519", "ssh-ed25519:[srv]:22");

        // BackgroundScheduler is not set, so LoadScheduler is the default inline one: the task
        // completes synchronously with the work lambda's catch result ([]).
        vm.Reload(preserveSelection: false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsLoading);
        Assert.DoesNotContain(vm.Rows, r => !r.IsParentNav);
    }

    [AvaloniaFact]
    public void RapidNavigation_supersedesStaleLoad_onlyFinalDirWins()
    {
        using var tmp = new TempDir();
        tmp.Dir("first");
        tmp.File("first/one.txt", "1");
        tmp.Dir("second");
        tmp.File("second/two.txt", "2");
        using var vm = new PaneViewModel(tmp.Path);

        var ms = new ManualScheduler();
        vm.LoadScheduler = ms.Schedule;

        vm.NavigateTo(Path.Combine(tmp.Path, "first"));
        vm.NavigateTo(Path.Combine(tmp.Path, "second"));  // load B supersedes A

        Assert.Equal(2, ms.Count);
        ms.Release(0); // A resolves late — its token was cancelled, must not apply
        ms.Release(1);

        Assert.Equal(Path.Combine(tmp.Path, "second"), vm.CurrentPath);
        Assert.Equal(["..", "two.txt"], vm.Rows.Select(r => r.Name));
    }
}
