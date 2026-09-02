using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Duetto.ViewModels;

public static class ViewerConverters
{
    public static readonly IValueConverter Wrapping =
        new FuncValueConverter<bool, TextWrapping>(wrapped => wrapped ? TextWrapping.Wrap : TextWrapping.NoWrap);

    public static readonly IValueConverter MatchBackground =
        new FuncValueConverter<bool, IBrush>(matched => matched
            ? PaletteLookup.Brush("MatchHighlight", "#f6e6a8")
            : Brushes.Transparent);
}
