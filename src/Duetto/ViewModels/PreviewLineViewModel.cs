using CommunityToolkit.Mvvm.ComponentModel;

namespace Duetto.ViewModels;

public partial class PreviewLineViewModel(int? number, string text) : ObservableObject
{
    public int? Number { get; } = number;

    public string NumberText { get; } = number?.ToString() ?? "";

    public string Text { get; } = text;

    [ObservableProperty]
    private bool _isMatch;
}
