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

        // Capture before InitializeComponent: the ComboBox's SelectedIndex="0" fires
        // OnProtocolChanged during load and resets vm.Protocol to Sftp, so read the intended
        // protocol (set by ForEdit) first, then reflect it in the dropdown afterwards.
        var startProtocol = vm.Protocol;
        InitializeComponent();
        ProtocolBox.SelectedIndex = startProtocol == ConnectProtocol.Smb ? 1 : 0;

        vm.Connected += _ => Close();
        vm.SmbConnected += _ => Close();
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

    // Uses `sender` rather than the ProtocolBox field: this fires during InitializeComponent
    // (ComboBox EndInit) before the named field is assigned, so the field is still null then.
    private void OnProtocolChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox box && DataContext is ConnectDialogViewModel vm)
            vm.Protocol = box.SelectedIndex == 1 ? ConnectProtocol.Smb : ConnectProtocol.Sftp;
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
