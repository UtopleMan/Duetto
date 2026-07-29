using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.ViewModels;
using Renci.SshNet.Common;

namespace Duetto.Views;

public partial class PaneView : UserControl
{
    private string _typeAhead = "";
    private DateTime _typeAheadAt = DateTime.MinValue;

    private PaneViewModel? _subscribedVm;

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm is { } old)
                old.Reloaded -= OnVmReloaded;
            _subscribedVm = Vm;
            if (_subscribedVm is { } newVm)
            {
                newVm.Reloaded += OnVmReloaded;
                newVm.Drives.CloseRequested += () => Dispatcher.UIThread.Post(HideDriveFlyout);
                newVm.Drives.ConnectRequested += () => Dispatcher.UIThread.Post(() =>
                    OpenConnectDialog(newVm, null));
                newVm.Drives.EditShareRequested += stored => Dispatcher.UIThread.Post(() =>
                    OpenConnectDialog(newVm, stored));
                newVm.Drives.RemoveShareRequested += id => Dispatcher.UIThread.Post(() =>
                    RemoveConnection(id));
                newVm.Drives.ShareActivated += share => Dispatcher.UIThread.Post(() =>
                    ActivateShare(newVm, share));
                newVm.Drives.DisconnectRequested += () => Dispatcher.UIThread.Post(() =>
                    DisconnectCurrentPane(newVm));
            }
        };

        // ⌘/Ctrl-click toggles a mark, Shift-click marks a range — before the
        // ListBox turns the click into a plain cursor move.
        RowList.AddHandler(PointerPressedEvent, OnRowPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || (e.Source as Control)?.DataContext is not FileRowViewModel row)
            return;
        var mods = e.KeyModifiers;
        if (mods.HasFlag(KeyModifiers.Meta) || mods.HasFlag(KeyModifiers.Control))
        {
            vm.ToggleMarkAt(row);
            e.Handled = true;
        }
        else if (mods.HasFlag(KeyModifiers.Shift))
        {
            vm.MarkRangeTo(row);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Reload replaces every row container; if the focused one died with it,
    /// keyboard focus becomes null. Restore it to this pane when it is active.
    /// The reload keeps a valid selected row (see <c>ApplyRows</c>), so the list
    /// always has a container to focus.
    /// </summary>
    private void OnVmReloaded() => Dispatcher.UIThread.Post(() =>
    {
        if (Vm is { IsActive: true } &&
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
            FocusList();
    });

    /// <summary>Focuses the selected row container, falling back to the list.</summary>
    public void FocusList()
    {
        var container = RowList.SelectedIndex >= 0 ? RowList.ContainerFromIndex(RowList.SelectedIndex) : null;
        if (container is null || !container.Focus())
            RowList.Focus();
    }

    private PaneViewModel? Vm => DataContext as PaneViewModel;

    /// <summary>Raised when the user interacts with this pane; MainWindow marks it active.</summary>
    public event Action<PaneView>? Interacted;

    public ListBox List => RowList;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Interacted?.Invoke(this);
    }

    private void OnListFocused(object? sender, GotFocusEventArgs e) => Interacted?.Invoke(this);

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is { } vm && (e.Source as Control)?.DataContext is FileRowViewModel row)
        {
            vm.Open(row);
            Dispatcher.UIThread.Post(() => RowList.Focus());
        }
    }

    private void OnSortName(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Name);
    private void OnSortSize(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Size);
    private void OnSortType(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Type);
    private void OnSortModified(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Modified);

    /// <summary>Type-ahead: printable characters jump the cursor to the first name match.</summary>
    public void TypeAhead(string symbol)
    {
        if (Vm is not { } vm)
            return;
        var now = DateTime.UtcNow;
        _typeAhead = (now - _typeAheadAt).TotalMilliseconds > 900 ? symbol : _typeAhead + symbol;
        _typeAheadAt = now;
        var hit = vm.Rows.FirstOrDefault(r => r.Name.StartsWith(_typeAhead, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            vm.SelectByName(hit.Name);
            RowList.ScrollIntoView(vm.Rows.IndexOf(hit));
        }
    }

    private void OnEditBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnEditBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FileRowViewModel row || Vm is not { } vm)
            return;
        if (e.Key == Key.Enter)
        {
            vm.CommitRename(row);
            RowList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRename(row);
            RowList.Focus();
            e.Handled = true;
        }
    }

    private void OnEditBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FileRowViewModel { IsEditing: true } row } && Vm is { } vm)
            vm.CommitRenameFromBlur(row);
    }

    private void OnVolumeChipClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        Interacted?.Invoke(this);
        vm.Drives.Refresh();
        // Flyout opens automatically (Button.Flyout); focus the filter box for type-to-filter.
        Dispatcher.UIThread.Post(() =>
        {
            DriveFilterBox.Focus();
            if (DriveList.ItemCount > 0 && DriveList.SelectedIndex < 0)
                DriveList.SelectedIndex = 0;
        });
    }

    private void OnDriveRowActivated(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (e.Source as Control)?.DataContext is VolumeRowViewModel row)
        {
            vm.Drives.OpenVolume(row);
            e.Handled = true;
        }
    }

    private void OnShareRowClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (sender as Control)?.DataContext is ShareRowViewModel share)
        {
            vm.Drives.ActivateShare(share);
            e.Handled = true;
        }
    }

    private void OnShareEditClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (sender as Control)?.DataContext is ShareRowViewModel share)
        {
            vm.Drives.EditShare(share);
            e.Handled = true;
        }
    }

    private void OnShareRemoveClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (sender as Control)?.DataContext is ShareRowViewModel share)
        {
            vm.Drives.RemoveShare(share);
            e.Handled = true;
        }
    }

    private void OnDriveFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        switch (e.Key)
        {
            case Key.Down:
                DriveList.SelectedIndex = Math.Min(DriveList.SelectedIndex + 1, DriveList.ItemCount - 1);
                e.Handled = true;
                break;
            case Key.Up:
                DriveList.SelectedIndex = Math.Max(DriveList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter when DriveList.SelectedItem is VolumeRowViewModel row:
                vm.Drives.OpenVolume(row);
                e.Handled = true;
                break;
            case Key.Escape:
                HideDriveFlyout();
                e.Handled = true;
                break;
            case Key.K when e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                vm.Drives.Connect();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Opens ConnectWindow, optionally pre-filled for editing an existing connection.
    /// On success navigates the pane to the connection root and rebuilds remote places.
    /// </summary>
    private void OpenConnectDialog(PaneViewModel paneVm, StoredConnection? forEdit)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        var dialogVm = new ConnectDialogViewModel(
            mainVm.ConnectionManager,
            mainVm.ConnectionStore,
            mainVm.HostKeyStore,
            mainVm.Codec);

        if (forEdit is not null)
            dialogVm.ForEdit(forEdit);

        dialogVm.Connected += info =>
        {
            var remotePath = $"sftp://{info.Id}{info.InitialRemotePath}";
            paneVm.NavigateTo(remotePath);
            mainVm.RebuildRemotePlaces();
        };

        new ConnectWindow(dialogVm).ShowDialog(owner);
    }

    /// <summary>
    /// Removes a saved connection from the store and rebuilds remote places.
    /// </summary>
    private void RemoveConnection(string id)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        // Any pane still on this share must leave it before the provider is torn down
        // (same behavior as the Disconnect row).
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var pane in new[] { mainVm.Left, mainVm.Right })
        {
            if (PathUtil.ParseRemote(pane.CurrentPath) is { } remote &&
                string.Equals(remote.Id, id, StringComparison.OrdinalIgnoreCase))
                pane.NavigateTo(home);
        }

        // Drop the live session and registry entry (no-op for unknown ids),
        // then remove the saved record.
        mainVm.ConnectionManager.Disconnect(id);
        var all = mainVm.ConnectionStore.Load().Where(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        mainVm.ConnectionStore.Save(all);
        mainVm.RebuildRemotePlaces();
    }

    /// <summary>
    /// Handles a share row click: if already connected navigate; if secret saved connect
    /// on background thread then navigate; otherwise open ConnectWindow prefilled.
    /// </summary>
    private void ActivateShare(PaneViewModel paneVm, ShareRowViewModel share)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        if (share.IsConnected)
        {
            // Already live: navigate directly.
            var path = $"sftp://{share.Id}{share.InitialRemotePath}";
            paneVm.NavigateTo(path);
            return;
        }

        // Try to resolve a saved secret.
        var stored = mainVm.ConnectionStore.Load()
            .FirstOrDefault(c => string.Equals(c.Id, share.Id, StringComparison.OrdinalIgnoreCase));
        if (stored is null)
        {
            OpenConnectDialog(paneVm, share.Stored);
            return;
        }

        var secret = ConnectionStore.ResolveSecret(stored, mainVm.Codec);
        if (secret is not null)
        {
            // Secret saved: connect on background thread then navigate. On failure
            // (stale password, changed host key, network) open the dialog prefilled
            // so the user retries there and sees the real error; never navigate.
            var info = ConnectionStore.ResolveInfo(stored);
            var capturedPath = $"sftp://{info.Id}{info.InitialRemotePath}";
            _ = Task.Run(() =>
            {
                try
                {
                    mainVm.ConnectionManager.Connect(info, secret);
                }
                catch (Exception ex) when (ex is SshAuthenticationException
                    or SshConnectionException
                    or SocketException
                    or HostKeyChangedException
                    or ObjectDisposedException
                    or SshException
                    or IOException
                    or InvalidOperationException)
                {
                    Dispatcher.UIThread.Post(() => OpenConnectDialog(paneVm, stored));
                    return;
                }
                Dispatcher.UIThread.Post(() => paneVm.NavigateTo(capturedPath));
            });
            return;
        }

        // No secret saved: open the dialog pre-filled so the user can enter it.
        OpenConnectDialog(paneVm, stored);
    }

    /// <summary>
    /// Disconnects the current remote pane and navigates it home.
    /// Parallel to the Eject behavior.
    /// </summary>
    private void DisconnectCurrentPane(PaneViewModel paneVm)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        if (PathUtil.ParseRemote(paneVm.CurrentPath) is not { } remote)
            return;

        mainVm.ConnectionManager.Disconnect(remote.Id);
        paneVm.NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>x:Name fields inside Flyout content can be unreliable; go via the chip.</summary>
    private void HideDriveFlyout()
    {
        VolumeChip.Flyout?.Hide();
        FocusList();
    }
}
