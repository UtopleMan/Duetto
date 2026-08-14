using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class ConnectWindow : Window
{
    private ConnectDialogViewModel Vm => (ConnectDialogViewModel)DataContext!;

    public ConnectWindow()
    {
        InitializeComponent();
    }

    public ConnectWindow(ConnectDialogViewModel vm)
    {
        DataContext = vm;

        var startProtocol = vm.Protocol;
        InitializeComponent();
        ProtocolBox.SelectedIndex = startProtocol switch
        {
            ConnectProtocol.Smb => 1,
            ConnectProtocol.S3 => 2,
            ConnectProtocol.AzureBlob => 3,
            _ => 0,
        };

        vm.Connected += _ => Close();
        vm.SmbConnected += _ => Close();
        vm.S3Connected += _ => Close();
        vm.AzureConnected += _ => Close();
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

    private void OnProtocolChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox box && DataContext is ConnectDialogViewModel vm)
            vm.Protocol = box.SelectedIndex switch
            {
                1 => ConnectProtocol.Smb,
                2 => ConnectProtocol.S3,
                3 => ConnectProtocol.AzureBlob,
                _ => ConnectProtocol.Sftp,
            };
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

    private void OnS3KeysModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.S3Auth = Core.Remote.S3AuthMode.Keys;

    private void OnS3ProfileModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.S3Auth = Core.Remote.S3AuthMode.Profile;

    private void OnS3AnonymousModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.S3Auth = Core.Remote.S3AuthMode.Anonymous;

    private void OnAzureSharedKeyModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.AzureAuth = Core.Remote.AzureAuthMode.SharedKey;

    private void OnAzureConnStringModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.AzureAuth = Core.Remote.AzureAuthMode.ConnectionString;

    private void OnAzureSasModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.AzureAuth = Core.Remote.AzureAuthMode.Sas;

    private void OnAzureAnonymousModeClicked(object? sender, RoutedEventArgs e) =>
        Vm.AzureAuth = Core.Remote.AzureAuthMode.Anonymous;

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
