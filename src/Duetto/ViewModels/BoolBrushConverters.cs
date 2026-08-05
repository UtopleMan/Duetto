using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Duetto.ViewModels;

// Exit-pill colors: green tint on success, red tint on failure. Theme-aware via PaletteLookup —
// the func runs per Convert (after startup), so it resolves the active theme's brush; the fallback
// hex is the light value, keeping behaviour identical when the palette isn't merged.
public static class BoolBrushConverters
{
    public static readonly IValueConverter ExitPillBg = Make("SuccessBg", "#e7f3ec", "DangerBg", "#f5e6e4");

    // Marked-row fill: design selection blue, else transparent.
    public static readonly IValueConverter MarkedBg = Make("SelectionBg", "#dfe8f7", null, "#00000000");
    public static readonly IValueConverter ExitPillBorder = Make("SuccessBorder", "#cfe6d9", "DangerBorder", "#eed7d4");
    public static readonly IValueConverter ExitPillText = Make("Green", "#2f8f5b", "DangerText", "#a03c3c");

    private static IValueConverter Make(string okKey, string okFallback, string? failKey, string failFallback) =>
        new FuncValueConverter<bool, IBrush>(b => b
            ? PaletteLookup.Brush(okKey, okFallback)
            : failKey is null ? Brushes.Transparent : PaletteLookup.Brush(failKey, failFallback));
}
