using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Duet.Core.FileSystem;
using Duet.Tests.Core;
using Duet.ViewModels;
using Duet.Views;

namespace Duet.Tests.Ui;

public class PaneTests
{
    [AvaloniaFact]
    public void Pane_lists_directory_sorted_dirs_first()
    {
        using var tmp = new TempDir();
        tmp.Dir("zeta");
        tmp.File("alpha.txt", "a");
        tmp.File("beta.md", "b");

        using var vm = new PaneViewModel(tmp.Path);

        Assert.Equal(["..", "zeta", "alpha.txt", "beta.md"], vm.Rows.Select(r => r.Name));
        Assert.True(vm.Rows[0].IsParentNav);
        Assert.Equal("3 items", vm.StatusText);
    }

    [AvaloniaFact]
    public void Sort_by_size_toggles_direction()
    {
        using var tmp = new TempDir();
        tmp.File("small.txt", "x");
        tmp.File("large.txt", new string('x', 500));

        using var vm = new PaneViewModel(tmp.Path);
        vm.SortBy(SortColumn.Size);
        Assert.Equal(["..", "small.txt", "large.txt"], vm.Rows.Select(r => r.Name));
        Assert.Contains("▲", vm.SizeHeader);

        vm.SortBy(SortColumn.Size);
        Assert.Equal(["..", "large.txt", "small.txt"], vm.Rows.Select(r => r.Name));
        Assert.Contains("▼", vm.SizeHeader);
    }

    [AvaloniaFact]
    public void Tab_switches_active_pane()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Left.IsActive);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Right.IsActive);
        Assert.False(vm.Left.IsActive);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.True(vm.Left.IsActive);
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_descends_into_directory()
    {
        using var tmp = new TempDir();
        tmp.File("sub/inner.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Left.SelectByName("sub");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Path.Combine(tmp.Path, "sub"), vm.Left.CurrentPath);
        Assert.Equal(["..", "inner.txt"], vm.Left.Rows.Select(r => r.Name));
        // Focus refocus after reload can't be asserted headlessly (Focus() is
        // rejected without an active window); covered by RefocusActiveList.
        window.Close();
    }

    [AvaloniaFact]
    public void Backspace_goes_up()
    {
        using var tmp = new TempDir();
        var sub = tmp.Dir("sub");
        using var vm = new MainViewModel(sub, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(tmp.Path, vm.Left.CurrentPath);
        window.Close();
    }

    [AvaloniaFact]
    public void Rename_via_viewmodel_renames_file()
    {
        using var tmp = new TempDir();
        tmp.File("old-name.txt", "data");
        using var vm = new PaneViewModel(tmp.Path);
        vm.SelectByName("old-name.txt");

        var row = vm.StartRename();
        Assert.NotNull(row);
        row!.EditName = "new-name.txt";
        vm.CommitRename(row);

        Assert.True(File.Exists(Path.Combine(tmp.Path, "new-name.txt")));
        Assert.False(File.Exists(Path.Combine(tmp.Path, "old-name.txt")));
        Assert.Contains(vm.Rows, r => r.Name == "new-name.txt");
    }

    [AvaloniaFact]
    public void NewFolder_creates_and_selects()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path);
        vm.NewFolder();

        Assert.True(Directory.Exists(Path.Combine(tmp.Path, "New folder")));
        Assert.Equal("New folder", (vm.Selection.SelectedItem as FileRowViewModel)?.Name);
    }

    [AvaloniaFact]
    public void Navigation_history_back_forward()
    {
        using var tmp = new TempDir();
        var sub = tmp.Dir("sub");
        using var vm = new PaneViewModel(tmp.Path);

        vm.NavigateTo(sub);
        Assert.True(vm.CanGoBack);
        vm.Back();
        Assert.Equal(tmp.Path, vm.CurrentPath);
        Assert.True(vm.CanGoForward);
        vm.Forward();
        Assert.Equal(sub, vm.CurrentPath);
    }

    [AvaloniaFact]
    public void Parent_row_navigates_up_and_selects_child_dir()
    {
        using var tmp = new TempDir();
        var sub = tmp.Dir("child");
        using var vm = new PaneViewModel(sub);

        var parentRow = vm.Rows[0];
        Assert.True(parentRow.IsParentNav);
        Assert.Equal("..", parentRow.Name);

        vm.Open(parentRow);
        Assert.Equal(tmp.Path, vm.CurrentPath);
        Assert.Equal("child", (vm.Selection.SelectedItem as FileRowViewModel)?.Name);
    }

    [AvaloniaFact]
    public void Parent_row_excluded_from_ops_and_rename()
    {
        using var tmp = new TempDir();
        tmp.File("real.txt", "x");
        using var vm = new PaneViewModel(tmp.Path);

        vm.Selection.Select(0); // ".."
        Assert.Empty(vm.SelectedRows);
        Assert.Null(vm.StartRename());
        Assert.Equal("1 item", vm.StatusText);
    }

    [AvaloniaFact]
    public void Startup_puts_cursor_on_first_row()
    {
        using var tmp = new TempDir();
        tmp.File("a.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var cursor = vm.Left.Selection.SelectedItem as FileRowViewModel;
        Assert.NotNull(cursor);
        Assert.True(cursor!.IsParentNav); // ".." is row 0 in a non-root dir
        window.Close();
    }

    [AvaloniaFact]
    public void Insert_marks_row_and_advances_cursor()
    {
        using var tmp = new TempDir();
        tmp.File("aaa.txt", "x");
        tmp.File("bbb.txt", "x");
        using var vm = new PaneViewModel(tmp.Path); // rows: .., aaa, bbb

        vm.Selection.Select(1);
        vm.ToggleMarkAndAdvance(); // aaa was selected -> unmark, cursor to bbb
        Assert.Empty(vm.SelectedRows);
        Assert.Equal(2, vm.Selection.AnchorIndex);

        vm.ToggleMarkAndAdvance(); // mark bbb, cursor stays on last row
        Assert.Equal(["bbb.txt"], vm.SelectedRows.Select(r => r.Name));
        Assert.Equal(2, vm.Selection.AnchorIndex);
    }

    [AvaloniaFact]
    public void Insert_steps_over_parent_row_without_marking()
    {
        using var tmp = new TempDir();
        tmp.File("aaa.txt", "x");
        using var vm = new PaneViewModel(tmp.Path); // rows: .., aaa

        vm.Selection.AnchorIndex = 0;
        vm.ToggleMarkAndAdvance(); // ".." skipped, cursor to aaa
        Assert.Empty(vm.SelectedRows);
        Assert.Equal(1, vm.Selection.AnchorIndex);

        vm.ToggleMarkAndAdvance();
        Assert.Equal(["aaa.txt"], vm.SelectedRows.Select(r => r.Name));
    }

    [AvaloniaFact]
    public void PageDown_Home_End_move_cursor()
    {
        using var tmp = new TempDir();
        for (var i = 0; i < 50; i++)
            tmp.File($"f-{i:d2}.txt", "x");
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Left.Selection.Clear();
        vm.Left.Selection.Select(0);
        window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterPage = vm.Left.Selection.SelectedIndex;
        Assert.True(afterPage > 0, $"cursor did not move (index {afterPage})");
        Assert.True(afterPage < vm.Left.Rows.Count - 1, "page jumped straight to end");

        window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.Left.Rows.Count - 1, vm.Left.Selection.SelectedIndex);

        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.Left.Selection.SelectedIndex);

        window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.Left.Selection.SelectedIndex); // clamped at top
        window.Close();
    }

    [AvaloniaFact]
    public void Launches_file_with_default_app_hook()
    {
        using var tmp = new TempDir();
        tmp.File("doc.txt", "x");
        using var vm = new PaneViewModel(tmp.Path);
        string? launched = null;
        vm.LaunchFile = p => launched = p;

        vm.SelectByName("doc.txt");
        vm.OpenCursor();
        Assert.Equal(Path.Combine(tmp.Path, "doc.txt"), launched);
    }
}
