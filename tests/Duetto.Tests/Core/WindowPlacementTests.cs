using Duetto.Core.State;
using Xunit;

namespace Duetto.Tests.Core;

public class WindowPlacementTests
{
    private static WindowPlacement At(int x, int y) => new(x, y, 800, 600, Maximized: false);

    [Fact]
    public void Corner_inside_single_screen_is_visible()
    {
        var screens = new[] { new ScreenBounds(0, 0, 1920, 1080) };
        Assert.True(At(100, 100).IsVisibleOn(screens));
    }

    [Fact]
    public void Corner_outside_all_screens_is_not_visible()
    {
        var screens = new[] { new ScreenBounds(0, 0, 1920, 1080) };
        Assert.False(At(5000, 3000).IsVisibleOn(screens));
    }

    [Fact]
    public void Corner_on_second_monitor_is_visible()
    {
        var screens = new[]
        {
            new ScreenBounds(0, 0, 1920, 1080),
            new ScreenBounds(1920, 0, 2560, 1440),
        };
        Assert.True(At(2400, 300).IsVisibleOn(screens));
    }

    [Fact]
    public void No_screens_is_not_visible()
    {
        Assert.False(At(100, 100).IsVisibleOn([]));
    }
}
