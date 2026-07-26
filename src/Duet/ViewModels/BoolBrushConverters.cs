using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Duet.ViewModels;

/// <summary>Exit-pill colors: green tint on success, red tint on failure.</summary>
public static class BoolBrushConverters
{
    public static readonly IValueConverter ExitPillBg = Make("#e7f3ec", "#f5e6e4");
    public static readonly IValueConverter ExitPillBorder = Make("#cfe6d9", "#eed7d4");
    public static readonly IValueConverter ExitPillText = Make("#2f8f5b", "#a03c3c");

    private static IValueConverter Make(string ok, string fail) =>
        new FuncValueConverter<bool, IBrush>(b => Brush.Parse(b ? ok : fail));
}
