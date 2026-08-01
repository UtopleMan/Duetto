using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class SmbConnectWindow : Window
{
    private SmbConnectDialogViewModel Vm => (SmbConnectDialogViewModel)DataContext!;

    // Parameterless constructor required by Avalonia's XAML resource loader.
    // Not used at runtime; production code always calls the overload that takes a VM.
    public SmbConnectWindow()
    {
        InitializeComponent();
    }

    public SmbConnectWindow(SmbConnectDialogViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();

        vm.Connected += _ => Close();
        vm.Cancelled += Close;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Vm.Cancel();
            e.Handled = true;
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Vm.Cancel();

    private async void OnConnectClicked(object? sender, RoutedEventArgs e) =>
        await Vm.ConnectAsync();
}
