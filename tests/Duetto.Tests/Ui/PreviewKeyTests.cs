using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;
using Duetto.Views;

namespace Duetto.Tests.Ui;

public class PreviewKeyTests
{
    private static (MainViewModel Vm, List<PreviewRequest> Requests) Wired(
        TempDir tmp, FileSystemRegistry? registry = null)
    {
        var vm = new MainViewModel(tmp.Path, tmp.Path, registry: registry);
        var requests = new List<PreviewRequest>();
        vm.OpenViewer = requests.Add;
        return (vm, requests);
    }

    [AvaloniaFact]
    public void File_row_previews_the_pane_qualified_address()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "hello");
        var (vm, requests) = Wired(tmp);

        vm.Left.SelectByName("note.txt");
        vm.PreviewCursor();

        var request = Assert.Single(requests);
        Assert.Equal(path, request.Address);
        Assert.Equal("note.txt", request.DisplayName);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Directory_row_previews_nothing()
    {
        using var tmp = new TempDir();
        tmp.Dir("folder");
        var (vm, requests) = Wired(tmp);

        vm.Left.SelectByName("folder");
        vm.PreviewCursor();

        Assert.Empty(requests);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Parent_row_previews_nothing()
    {
        using var tmp = new TempDir();
        tmp.File("note.txt", "hello");
        var (vm, requests) = Wired(tmp);

        vm.Left.Selection.Select(0);
        Assert.True(vm.Left.CursorRow!.IsParentNav);
        vm.PreviewCursor();

        Assert.Empty(requests);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Empty_selection_previews_nothing()
    {
        using var tmp = new TempDir();
        var (vm, requests) = Wired(tmp);

        vm.Left.Selection.Clear();
        vm.PreviewCursor();

        Assert.Empty(requests);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Remote_pane_previews_a_scheme_qualified_address()
    {
        using var tmp = new TempDir();
        var remote = new InMemoryFileSystemProvider();
        remote.CreateFile("/", "note.txt");
        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv", remote);

        var (vm, requests) = Wired(tmp, registry);
        vm.Left.NavigateTo("sftp://srv/");
        Dispatcher.UIThread.RunJobs();
        vm.Left.SelectByName("note.txt");
        vm.PreviewCursor();

        Assert.Equal("sftp://srv/note.txt", Assert.Single(requests).Address);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task Focused_search_result_is_previewed_instead_of_the_pane_cursor()
    {
        using var tmp = new TempDir();
        tmp.File("pane.txt", "a");
        var hit = tmp.File("only-hit.txt", "b");
        var (vm, requests) = Wired(tmp);

        vm.Left.SelectByName("pane.txt");
        vm.Search.Query = "only-hit";
        await vm.Search.StartSearchAsync();

        vm.Search.Selection.Select(0);
        vm.SearchResultsFocused = () => true;
        vm.PreviewCursor();

        Assert.Equal(hit, Assert.Single(requests).Address);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task Unfocused_search_results_leave_the_pane_cursor_in_charge()
    {
        using var tmp = new TempDir();
        var pane = tmp.File("pane.txt", "a");
        tmp.File("only-hit.txt", "b");
        var (vm, requests) = Wired(tmp);

        vm.Left.SelectByName("pane.txt");
        vm.Search.Query = "only-hit";
        await vm.Search.StartSearchAsync();

        vm.Search.Selection.Select(0);
        vm.SearchResultsFocused = () => false;
        vm.PreviewCursor();

        Assert.Equal(pane, Assert.Single(requests).Address);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Preview_is_not_gated_by_a_running_operation()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "hello");
        var (vm, requests) = Wired(tmp);
        vm.ActiveOperation = new SimpleOperationViewModel("Copying…", new CancellationTokenSource());

        vm.Left.SelectByName("note.txt");
        vm.PreviewCursor();

        Assert.Equal(path, Assert.Single(requests).Address);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void F3_on_the_main_window_reaches_preview_cursor()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "hello");
        var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var requests = new List<PreviewRequest>();
        vm.OpenViewer = requests.Add;
        vm.Left.SelectByName("note.txt");

        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(path, Assert.Single(requests).Address);
        window.Close();
    }

    [AvaloniaFact]
    public void One_viewer_window_is_reused_across_previews()
    {
        using var tmp = new TempDir();
        tmp.File("first.txt", "one");
        tmp.File("second.txt", "two");
        var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Left.SelectByName("first.txt");
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var firstViewer = window.Viewer;

        vm.Left.SelectByName("second.txt");
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(firstViewer);
        Assert.Same(firstViewer, window.Viewer);
        Assert.Equal("second.txt", window.Viewer!.Vm.FileName);
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_the_main_window_closes_the_viewer()
    {
        using var tmp = new TempDir();
        tmp.File("note.txt", "one");
        var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Left.SelectByName("note.txt");
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.Viewer);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(window.Viewer);
    }

    [AvaloniaFact]
    public void Viewer_open_in_default_app_launches_a_local_path()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "one");
        var vm = new MainViewModel(tmp.Path, tmp.Path);
        var launched = new List<string>();
        vm.Left.LaunchFile = launched.Add;
        vm.Right.LaunchFile = launched.Add;
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Left.SelectByName("note.txt");
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.Viewer!.Vm.OpenInDefaultAppCommand.Execute(null);

        Assert.Equal(path, Assert.Single(launched));
        window.Close();
    }
}
