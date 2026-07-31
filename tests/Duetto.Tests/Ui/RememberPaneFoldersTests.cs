using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.State;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Duetto.Views;
using Xunit;

namespace Duetto.Tests.Ui;

public class RememberPaneFoldersTests
{
    private sealed class Box { public string? Value; }

    private static SessionStore InMemory(Box box) =>
        new("mem", _ => box.Value, (_, c) => box.Value = c);

    [AvaloniaFact]
    public void SaveSession_persists_current_pane_dirs()
    {
        using var home = new TempDir();
        using var leftDir = new TempDir();
        var box = new Box();
        using var vm = new MainViewModel(home.Path, home.Path, sessionStore: InMemory(box));

        vm.Left.NavigateTo(leftDir.Path);
        vm.SaveSession();

        var saved = InMemory(box).Load();
        Assert.NotNull(saved);
        Assert.Equal(leftDir.Path, saved!.LeftPath);
        Assert.Equal(home.Path, saved.RightPath);
    }

    [AvaloniaFact]
    public void Closing_the_window_saves_the_session()
    {
        using var home = new TempDir();
        using var leftDir = new TempDir();
        var box = new Box();
        var vm = new MainViewModel(home.Path, home.Path, sessionStore: InMemory(box));
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Left.NavigateTo(leftDir.Path);
        window.Close();
        Dispatcher.UIThread.RunJobs();

        var saved = InMemory(box).Load();
        Assert.NotNull(saved);
        Assert.Equal(leftDir.Path, saved!.LeftPath);
    }
}
