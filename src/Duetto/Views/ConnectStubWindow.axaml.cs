using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Duetto.Views;

public partial class ConnectStubWindow : Window
{
    public ConnectStubWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
