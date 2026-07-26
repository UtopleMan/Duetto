using Avalonia.Controls;
using Avalonia.Input;
using Duet.ViewModels;

namespace Duet.Views;

public partial class SearchResultsView : UserControl
{
    public SearchResultsView()
    {
        InitializeComponent();
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SearchViewModel vm &&
            (e.Source as Control)?.DataContext is SearchResultRowViewModel)
            vm.RevealSelected();
    }
}
