using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duet.Tests.Core;
using Duet.ViewModels;
using Duet.Views;

namespace Duet.Tests.Ui;

public class SearchDebounceProbe
{
    [AvaloniaFact]
    public async Task Typing_query_triggers_debounced_search_and_overlay()
    {
        using var tmp = new TempDir();
        tmp.File("deep/nested/found-needle.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Simulate typing via the bound property, letting the 300 ms debounce fire.
        vm.Search.Query = "needle";
        for (var i = 0; i < 60 && vm.Search.Results.Count == 0; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(vm.Search.IsActive, "search never activated from debounce");
        Assert.Single(vm.Search.Results);
        Assert.Equal("found-needle.txt", vm.Search.Results[0].Name);
        window.Close();
    }
}
