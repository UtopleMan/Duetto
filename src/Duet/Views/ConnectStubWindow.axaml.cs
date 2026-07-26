using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Duet.Views;

public partial class ConnectStubWindow : Window
{
    public ConnectStubWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
