using Avalonia.Controls;
using Avalonia.Input;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class SearchResultsView : UserControl
{
    public SearchResultsView()
    {
        InitializeComponent();
    }

    public void FocusList()
    {
        var container = ResultList.SelectedIndex >= 0 ? ResultList.ContainerFromIndex(ResultList.SelectedIndex) : null;
        if (container is null || !container.Focus())
            ResultList.Focus();
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SearchViewModel vm &&
            (e.Source as Control)?.DataContext is SearchResultRowViewModel)
            vm.RevealSelected();
    }
}
