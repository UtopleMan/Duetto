using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Core.State;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Duetto.Views;

namespace Duetto.Tests.Ui;

public class WindowPlacementUiTests
{
    private static Func<IReadOnlyList<ScreenBounds>> OneBigScreen =>
        () => [new ScreenBounds(0, 0, 1920, 1080)];

    private sealed class StringBox { public string? Value; }

    [AvaloniaFact]
    public void Saved_size_is_restored_on_open()
    {
        using var tmp = new TempDir();
        var box = new StringBox
        {
            Value = JsonSerializer.Serialize(new WindowPlacement(150, 120, 900, 650, Maximized: false)),
        };
        var store = new WindowPlacementStore("mem", _ => box.Value, (_, c) => box.Value = c);

        var window = new MainWindow(new MainViewModel(tmp.Path, tmp.Path), store, OneBigScreen);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(900, window.Width);
        Assert.Equal(650, window.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Placement_is_saved_on_close()
    {
        using var tmp = new TempDir();
        var box = new StringBox { Value = null };
        var store = new WindowPlacementStore("mem", _ => box.Value, (_, c) => box.Value = c);

        var window = new MainWindow(new MainViewModel(tmp.Path, tmp.Path), store, OneBigScreen);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(store.Load());
    }
}
