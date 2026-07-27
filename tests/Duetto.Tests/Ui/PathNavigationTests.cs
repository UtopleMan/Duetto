using Avalonia.Headless.XUnit;
using Duetto.Tests.Core;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class PathNavigationTests
{
    [AvaloniaFact]
    public void Absolute_directory_path_navigates_active_pane()
    {
        using var tmp = new TempDir();
        var sub = tmp.Dir("target");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        Assert.True(vm.TryNavigatePath(sub));
        Assert.Equal(sub, vm.ActivePane.CurrentPath);
        Assert.Equal("", vm.Search.Query);
        Assert.False(vm.Search.IsActive);
    }

    [AvaloniaFact]
    public void Relative_path_resolves_against_active_pane()
    {
        using var tmp = new TempDir();
        tmp.File("sub/inner/deep.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        Assert.True(vm.TryNavigatePath(Path.Combine("sub", "inner")));
        Assert.Equal(Path.Combine(tmp.Path, "sub", "inner"), vm.ActivePane.CurrentPath);
    }

    [AvaloniaFact]
    public void File_path_reveals_file_in_parent()
    {
        using var tmp = new TempDir();
        var file = tmp.File("docs/readme.md", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        Assert.True(vm.TryNavigatePath(file));
        Assert.Equal(Path.Combine(tmp.Path, "docs"), vm.ActivePane.CurrentPath);
        Assert.Equal("readme.md", (vm.ActivePane.Selection.SelectedItem as FileRowViewModel)?.Name);
    }

    [AvaloniaFact]
    public void Tilde_expands_to_home()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        Assert.True(vm.TryNavigatePath("~"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), vm.ActivePane.CurrentPath);
    }

    [AvaloniaFact]
    public void Nonexistent_path_returns_false_and_stays_put()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        Assert.False(vm.TryNavigatePath("/definitely/not/a/real/path-zzz"));
        Assert.Equal(tmp.Path, vm.ActivePane.CurrentPath);
    }

    [AvaloniaFact]
    public async Task Path_like_query_does_not_trigger_search_overlay()
    {
        using var tmp = new TempDir();
        tmp.File("hit.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.Search.Query = "/Users/somewhere";
        await vm.Search.StartSearchAsync();

        Assert.False(vm.Search.IsActive);
        Assert.Empty(vm.Search.Results);
        Assert.True(SearchViewModel.IsPathLike("~/x"));
        Assert.True(SearchViewModel.IsPathLike("sub/inner"));
        Assert.False(SearchViewModel.IsPathLike("plain-name"));
    }
}
