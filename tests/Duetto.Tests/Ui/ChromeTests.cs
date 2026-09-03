using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Duetto.Views;

namespace Duetto.Tests.Ui;

public class ChromeTests
{
    private static MainWindow Show(ChromeKind chrome, TempDir tmp)
    {
        var vm = new MainViewModel(tmp.Path, tmp.Path, chrome);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Win_chrome_shows_title_bar_no_rail()
    {
        using var tmp = new TempDir();
        var window = Show(ChromeKind.Win, tmp);

        Assert.True(window.FindControl<Border>("WinTitleBar")!.IsVisible);
        Assert.False(window.FindControl<Border>("GnomeHeader")!.IsVisible);
        Assert.False(window.FindControl<Border>("PlacesRail")!.IsVisible);
        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
        window.Close();
    }

    [AvaloniaFact]
    public void Mac_chrome_uses_cards_on_desk()
    {
        using var tmp = new TempDir();
        var window = Show(ChromeKind.Mac, tmp);

        Assert.False(window.FindControl<Border>("WinTitleBar")!.IsVisible);
        Assert.False(window.FindControl<Border>("PlacesRail")!.IsVisible);
        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        var card = window.FindControl<Border>("LeftCard")!;
        Assert.Equal(9, card.CornerRadius.TopLeft);
        Assert.Contains("·", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void Gnome_chrome_shows_header_and_places_rail()
    {
        using var tmp = new TempDir();
        var window = Show(ChromeKind.Gnome, tmp);

        Assert.True(window.FindControl<Border>("GnomeHeader")!.IsVisible);
        Assert.True(window.FindControl<Border>("PlacesRail")!.IsVisible);
        Assert.False(window.FindControl<Border>("WinTitleBar")!.IsVisible);
        Assert.NotEmpty(window.Vm.Places);
        window.Close();
    }

    [AvaloniaFact]
    public void Place_navigates_active_pane()
    {
        using var tmp = new TempDir();
        var sub = tmp.Dir("dest");
        var vm = new MainViewModel(tmp.Path, tmp.Path, ChromeKind.Gnome);
        vm.NavigatePlace(new Place("dest", sub, "#c8992f"));
        Assert.Equal(sub, vm.ActivePane.CurrentPath);
        vm.Dispose();
    }
}
