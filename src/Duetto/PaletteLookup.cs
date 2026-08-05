using Avalonia;
using Avalonia.Media;

namespace Duetto;

// Resolves a palette brush key against the active theme for colours that view models emit as
// strings (bound directly to Background/Foreground). Restart-to-apply means the palette is fixed
// for the session, so a lookup per binding-eval is cheap. Falls back to the given light hex when
// the app/resource is unavailable (e.g. headless tests without the palette merged), which keeps
// VM colour output byte-identical to the old hard-coded light values.
public static class PaletteLookup
{
    // Returns lowercase "#rrggbb" (or "#aarrggbb" when translucent) to match the previous literals.
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
