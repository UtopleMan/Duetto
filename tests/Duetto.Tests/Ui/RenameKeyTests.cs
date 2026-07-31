using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Duetto.Views;

namespace Duetto.Tests.Ui;

public class RenameKeyTests
{
    private static (MainWindow window, MainViewModel vm, FileRowViewModel row) StartRename(TempDir tmp)
    {
        tmp.File("f.txt", "x");
        var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Left.SelectByName("f.txt");
        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var row = vm.Left.Rows.First(r => r.Name == "f.txt");
        Assert.True(row.IsEditing); // precondition: F2 started rename
        return (window, vm, row);
    }

    [AvaloniaFact]
    public void Escape_cancels_rename()
    {
        using var tmp = new TempDir();
        var (window, _, row) = StartRename(tmp);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(row.IsEditing);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "f.txt"))); // unchanged
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_commits_rename()
    {
        using var tmp = new TempDir();
        var (window, _, row) = StartRename(tmp);

        row.EditName = "renamed.txt";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(row.IsEditing);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "renamed.txt")));
        Assert.False(File.Exists(Path.Combine(tmp.Path, "f.txt")));
        window.Close();
    }
}
