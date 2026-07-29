using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class ConnectWindow : Window
{
    private ConnectDialogViewModel Vm => (ConnectDialogViewModel)DataContext!;

    // Parameterless constructor required by Avalonia's XAML resource loader.
    // Not used at runtime; production code always calls the overload that takes a VM.
    public ConnectWindow()
    {
        InitializeComponent();
    }

    public ConnectWindow(ConnectDialogViewModel vm)
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

    private async void OnAcceptNewKey(object? sender, RoutedEventArgs e) =>
        await Vm.AcceptNewKeyAsync();

    private void OnPasswordModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.AuthMode = Core.Remote.AuthMode.Password;

    private void OnKeyModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.AuthMode = Core.Remote.AuthMode.Key;

    private async void OnBrowseKeyFile(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select private key file",
            AllowMultiple = false,
        });

        if (files is [{ } file])
            Vm.KeyPath = file.TryGetLocalPath() ?? Vm.KeyPath;
    }
}
