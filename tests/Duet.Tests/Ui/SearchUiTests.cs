using Avalonia.Headless.XUnit;
using Duet.Tests.Core;
using Duet.ViewModels;

namespace Duet.Tests.Ui;

public class SearchUiTests
{
    [AvaloniaFact]
    public async Task Search_finds_nested_files_and_reports_counts()
    {
        using var tmp = new TempDir();
        tmp.File("src/Views/MainWindow.axaml", "<Window/>");
        tmp.File("src/Controls/FileGrid.axaml.cs", "class C {}");
        tmp.File("readme.md", "hi");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "axaml";
        await vm.Search.StartSearchAsync();

        Assert.True(vm.Search.IsActive);
        Assert.Equal(2, vm.Search.Results.Count);
        Assert.Contains("2 matches", vm.Search.MatchText);
        Assert.NotEqual("", vm.Search.ElapsedText);
        var window = Assert.Single(vm.Search.Results, r => r.Name == "MainWindow.axaml");
        Assert.Equal(Path.Combine("src", "Views"), window.Folder);
    }

    [AvaloniaFact]
    public async Task Contents_toggle_finds_text_matches()
    {
        using var tmp = new TempDir();
        tmp.File("notes.txt", "the treasure is here");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "treasure";
        await vm.Search.StartSearchAsync();
        Assert.Empty(vm.Search.Results);

        vm.Search.IncludeContents = true;
        await vm.Search.StartSearchAsync();
        Assert.Equal(["notes.txt"], vm.Search.Results.Select(r => r.Name));
    }

    [AvaloniaFact]
    public async Task Size_filter_drops_small_files()
    {
        using var tmp = new TempDir();
        tmp.File("big-match.bin", new string('x', 2 * 1024 * 1024));
        tmp.File("small-match.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "match";
        vm.Search.SizeFilter = SizeFilter.Over1MB;
        await vm.Search.StartSearchAsync();

        Assert.Equal(["big-match.bin"], vm.Search.Results.Select(r => r.Name));
    }

    [AvaloniaFact]
    public async Task Reveal_navigates_left_pane_and_selects()
    {
        using var tmp = new TempDir();
        tmp.File("deep/nested/target.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "target";
        await vm.Search.StartSearchAsync();
        vm.Search.Selection.Select(0);
        vm.Search.RevealSelected();

        Assert.Equal(Path.Combine(tmp.Path, "deep", "nested"), vm.Left.CurrentPath);
        Assert.Equal("target.txt", (vm.Left.Selection.SelectedItem as FileRowViewModel)?.Name);
        Assert.True(vm.Left.IsActive);
    }

    [AvaloniaFact]
    public async Task Clear_deactivates_results_overlay()
    {
        using var tmp = new TempDir();
        tmp.File("hit.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "hit";
        await vm.Search.StartSearchAsync();
        Assert.True(vm.Search.IsActive);

        vm.Search.Clear();
        Assert.False(vm.Search.IsActive);
        Assert.Empty(vm.Search.Results);
        Assert.Equal("", vm.Search.Query);
    }

    [AvaloniaFact]
    public async Task Copy_from_results_lands_in_left_pane_dir()
    {
        using var scope = new TempDir();
        using var leftDir = new TempDir();
        scope.File("inner/wanted.txt", "payload");
        using var vm = new MainViewModel(leftDir.Path, scope.Path);
        vm.Activate(vm.Right); // search scopes to the right pane's dir

        vm.Search.Query = "wanted";
        await vm.Search.StartSearchAsync();
        Assert.Single(vm.Search.Results);
        vm.Search.Selection.Select(0);

        vm.CopySelected();
        Assert.NotNull(vm.ActiveTransfer);
        await vm.ActiveTransfer!.Session.Completion;

        Assert.Equal("payload", File.ReadAllText(Path.Combine(leftDir.Path, "wanted.txt")));
    }

    [AvaloniaFact]
    public async Task Pin_keeps_results_after_query_clears()
    {
        using var tmp = new TempDir();
        tmp.File("pin-me.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "pin";
        await vm.Search.StartSearchAsync();
        Assert.Single(vm.Search.Results);

        vm.Search.PinResults();
        await vm.Search.StartSearchAsync(); // debounce would fire with the now-empty query

        Assert.True(vm.Search.IsPinned);
        Assert.True(vm.Search.IsActive);
        Assert.Single(vm.Search.Results);
        Assert.Equal("", vm.Search.Query);

        vm.Search.Clear();
        Assert.False(vm.Search.IsActive);
    }

    [AvaloniaFact]
    public async Task Delete_from_results_trashes_and_removes_row()
    {
        using var tmp = new TempDir();
        var doomed = tmp.File("bye.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "bye";
        await vm.Search.StartSearchAsync();
        vm.Search.Selection.Select(0);

        vm.DeleteSelected();

        Assert.False(File.Exists(doomed));
        Assert.Empty(vm.Search.Results);
    }
}
