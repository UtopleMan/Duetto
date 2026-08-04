using Avalonia.Platform;
using Avalonia.Styling;
using Duetto;
using Duetto.Core.State;

namespace Duetto.Tests.Ui;

public class ThemeResolverTests
{
    private static ThemeVariant Expected(string s) => s == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    [Theory]
    [InlineData(AppTheme.Light, PlatformThemeVariant.Dark, "Light")]
    [InlineData(AppTheme.Dark, PlatformThemeVariant.Light, "Dark")]
    public void Explicit_setting_wins_over_os(AppTheme setting, PlatformThemeVariant os, string expected)
    {
        var (variant, uri) = ThemeResolver.Resolve(setting, os);

        Assert.Equal(Expected(expected), variant);
        Assert.Contains(expected, uri.ToString());
    }

    [Theory]
    [InlineData(PlatformThemeVariant.Light, "Light")]
    [InlineData(PlatformThemeVariant.Dark, "Dark")]
    public void System_follows_the_os_variant(PlatformThemeVariant os, string expected)
    {
        var (variant, uri) = ThemeResolver.Resolve(AppTheme.System, os);

        Assert.Equal(Expected(expected), variant);
        Assert.Contains(expected, uri.ToString());
    }
}
