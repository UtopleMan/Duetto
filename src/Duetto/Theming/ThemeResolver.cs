using Avalonia.Platform;
using Avalonia.Styling;
using Duetto.Core.State;

namespace Duetto;

public static class ThemeResolver
{
    private static readonly Uri LightPalette = new("avares://Duetto/Themes/Palette.Light.axaml");
    private static readonly Uri DarkPalette = new("avares://Duetto/Themes/Palette.Dark.axaml");

    public static (ThemeVariant Variant, Uri PaletteUri) Resolve(AppTheme setting, PlatformThemeVariant os)
    {
        var dark = setting switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => os == PlatformThemeVariant.Dark,
        };

        return dark
            ? (ThemeVariant.Dark, DarkPalette)
            : (ThemeVariant.Light, LightPalette);
    }
}
