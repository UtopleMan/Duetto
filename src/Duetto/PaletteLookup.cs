using Avalonia;
using Avalonia.Media;

namespace Duetto;

public static class PaletteLookup
{
    public static string Hex(string key, string fallbackHex)
    {
        if (TryBrush(key, out var b))
        {
            var c = b.Color;
            return c.A == 0xFF
                ? $"#{c.R:x2}{c.G:x2}{c.B:x2}"
                : $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}";
        }

        return fallbackHex;
    }

    public static IBrush Brush(string key, string fallbackHex) =>
        TryBrush(key, out var b) ? b : Avalonia.Media.Brush.Parse(fallbackHex);

    private static bool TryBrush(string key, out ISolidColorBrush brush)
    {
        var app = Application.Current;
        if (app is not null
            && app.Resources.TryGetResource(key, app.ActualThemeVariant, out var res)
            && res is ISolidColorBrush b)
        {
            brush = b;
            return true;
        }

        brush = default!;
        return false;
    }
}
