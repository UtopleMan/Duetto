using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Tests.Core;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class NewEntryTests
{
    private static FileRowViewModel Placeholder(PaneViewModel vm) =>
        vm.Rows.Single(r => r.IsNewPlaceholder);

    [AvaloniaFact]
    public void NewFolder_commit_creates_folder_with_typed_name()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path);
        vm.NewFolder();

        var row = Placeholder(vm);
        row.EditName = "Photos";
        vm.CommitRename(row);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Directory.Exists(Path.Combine(tmp.Path, "Photos")));
        Assert.DoesNotContain(vm.Rows, r => r.IsNewPlaceholder);
        Assert.Equal("Photos", (vm.Selection.SelectedItem as FileRowViewModel)?.Name);
    }

    [AvaloniaFact]
    public void NewFile_placeholder_is_a_file_and_commit_creates_file()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path);
        vm.NewFile();

        var row = Placeholder(vm);
        Assert.False(row.IsDirectory);
        Assert.Equal("New file", row.EditName);

        row.EditName = "notes.txt";
        vm.CommitRename(row);
        Dispatcher.UIThread.RunJobs();

        Assert.True(File.Exists(Path.Combine(tmp.Path, "notes.txt")));
        Assert.Equal("notes.txt", (vm.Selection.SelectedItem as FileRowViewModel)?.Name);
    }

    [AvaloniaFact]
    public void Commit_empty_name_discards_placeholder_and_creates_nothing()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path);
        vm.NewFolder();

        var row = Placeholder(vm);
        row.EditName = "   ";
        vm.CommitRename(row);

        Assert.DoesNotContain(vm.Rows, r => r.IsNewPlaceholder);
        Assert.Empty(Directory.GetFileSystemEntries(tmp.Path));
    }

    [AvaloniaFact]
    public void Cancel_discards_placeholder_and_creates_nothing()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path);
        vm.NewFolder();

        vm.CancelRename(Placeholder(vm));

        Assert.DoesNotContain(vm.Rows, r => r.IsNewPlaceholder);
        Assert.Empty(Directory.GetFileSystemEntries(tmp.Path));
    }

    [AvaloniaFact]
    public void Commit_colliding_name_keeps_editing_and_creates_nothing()
    {
        using var tmp = new TempDir();
        tmp.Dir("Existing");
        using var vm = new PaneViewModel(tmp.Path);
        vm.NewFolder();

        var row = Placeholder(vm);
        row.EditName = "Existing";
        vm.CommitRename(row);

        Assert.True(row.IsEditing);
        Assert.Contains(vm.Rows, r => r.IsNewPlaceholder);
        Assert.Single(Directory.GetDirectories(tmp.Path));
    }

    [AvaloniaFact]
    public void Placeholder_survives_an_unrelated_reload()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path);
        vm.NewFolder();
        var before = Placeholder(vm);

        vm.Reload(preserveSelection: true); // e.g. a FileSystemWatcher tick
        Dispatcher.UIThread.RunJobs();

        var after = Placeholder(vm);
        Assert.True(after.IsEditing);
        Assert.Equal(before.EditName, after.EditName);
    }
}
