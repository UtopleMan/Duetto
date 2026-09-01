using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class PaneView : UserControl
{
    private string _typeAhead = "";
    private DateTime _typeAheadAt = DateTime.MinValue;

    private PaneViewModel? _subscribedVm;

    private static readonly DataFormat<string> PaneDragFormat =
        DataFormat.CreateStringApplicationFormat("duetto.pane-source");

    private const double DragThreshold = 4;

    private Point? _dragOrigin;
    private PointerPressedEventArgs? _dragTrigger;

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
                newVm.Drives.EditSmbShareRequested += stored => Dispatcher.UIThread.Post(() =>
                    OpenSmbConnectDialog(newVm, stored));
                newVm.Drives.RemoveSmbShareRequested += id => Dispatcher.UIThread.Post(() =>
                    RemoveSmbConnection(id));
                newVm.Drives.EditS3ShareRequested += stored => Dispatcher.UIThread.Post(() =>
                    OpenS3ConnectDialog(newVm, stored));
                newVm.Drives.RemoveS3ShareRequested += id => Dispatcher.UIThread.Post(() =>
                    RemoveS3Connection(id));
                newVm.Drives.EditAzureShareRequested += stored => Dispatcher.UIThread.Post(() =>
                    OpenAzureConnectDialog(newVm, stored));
                newVm.Drives.RemoveAzureShareRequested += id => Dispatcher.UIThread.Post(() =>
                    RemoveAzureConnection(id));
                newVm.Drives.ShareActivated += share => Dispatcher.UIThread.Post(() =>
                    ActivateShare(newVm, share));
                newVm.Drives.DisconnectRequested += () => Dispatcher.UIThread.Post(() =>
                    DisconnectCurrentPane(newVm));
            }
        };

        RowList.AddHandler(PointerPressedEvent, OnRowPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerMovedEvent, OnRowPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerReleasedEvent, OnRowPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainViewModel? MainVm =>
        TopLevel.GetTopLevel(this) is Window { DataContext: MainViewModel mainVm } ? mainVm : null;

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
        else if (!row.IsParentNav && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragOrigin = e.GetPosition(this);
            _dragTrigger = e;
        }
    }

    private void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragOrigin is not { } origin || _dragTrigger is not { } trigger)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearDragStart();
            return;
        }

        var moved = e.GetPosition(this) - origin;
        if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            return;

        ClearDragStart();
        _ = StartPaneDragAsync(trigger);
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e) => ClearDragStart();

    private void ClearDragStart()
    {
        _dragOrigin = null;
        _dragTrigger = null;
    }

    private async Task StartPaneDragAsync(PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || MainVm is not { } mainVm)
            return;

        var side = ReferenceEquals(vm, mainVm.Left) ? "left" : "right";
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(PaneDragFormat, side));

        if (mainVm.LocalDragPayload(vm) is { } localPaths &&
            TopLevel.GetTopLevel(this)?.StorageProvider is { } storage)
        {
            foreach (var path in localPaths)
            {
                if (await ToStorageItem(storage, path) is { } item)
                    data.Add(DataTransferItem.CreateFile(item));
            }
        }

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private static async Task<IStorageItem?> ToStorageItem(IStorageProvider storage, string path) =>
        Directory.Exists(path)
            ? await storage.TryGetFolderFromPathAsync(path)
            : await storage.TryGetFileFromPathAsync(path);

    private void OnDragEnter(object? sender, DragEventArgs e) => UpdateDropFeedback(e);

    private void OnDragOver(object? sender, DragEventArgs e) => UpdateDropFeedback(e);

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Vm is { } vm)
            vm.IsDropTarget = false;
    }

    private void UpdateDropFeedback(DragEventArgs e)
    {
        var effect = ResolveDropEffect(e);
        e.DragEffects = effect;
        if (Vm is { } vm)
            vm.IsDropTarget = effect != DragDropEffects.None;
        e.Handled = true;
    }

    private DragDropEffects ResolveDropEffect(DragEventArgs e)
    {
        if (Vm is not { } targetVm || MainVm is not { } mainVm)
            return DragDropEffects.None;
        if (mainVm.ActiveOperation is { IsFinished: false })
            return DragDropEffects.None;

        var move = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.DataTransfer.TryGetValue(PaneDragFormat) is { } side)
        {
            var sourcePane = side == "left" ? mainVm.Left : mainVm.Right;
            if (ReferenceEquals(sourcePane, targetVm))
                return DragDropEffects.None;
            return move ? DragDropEffects.Move : DragDropEffects.Copy;
        }

        if (e.DataTransfer.TryGetFiles() is { Length: > 0 })
            return move ? DragDropEffects.Move : DragDropEffects.Copy;

        return DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm is not { } targetVm)
            return;
        targetVm.IsDropTarget = false;
        if (MainVm is not { } mainVm)
            return;

        var move = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.DataTransfer.TryGetValue(PaneDragFormat) is { } side)
        {
            var sourcePane = side == "left" ? mainVm.Left : mainVm.Right;
            mainVm.DropBetweenPanes(sourcePane, targetVm, move);
            return;
        }

        var osPaths = OsFilePaths(e);
        if (osPaths.Count > 0)
            mainVm.DropFromOs(targetVm, osPaths, move);
    }

    private static List<string> OsFilePaths(DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files)
            return [];
        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }

    private void OnVmReloaded() => Dispatcher.UIThread.Post(() =>
    {
        if (Vm is { IsActive: true } &&
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
            FocusList();
    });

    public void FocusList()
    {
        var container = RowList.SelectedIndex >= 0 ? RowList.ContainerFromIndex(RowList.SelectedIndex) : null;
        if (container is null || !container.Focus())
            RowList.Focus();
    }

    private PaneViewModel? Vm => DataContext as PaneViewModel;

    public event Action<PaneView>? Interacted;

    public ListBox List => RowList;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Interacted?.Invoke(this);
    }

    private void OnListFocused(object? sender, FocusChangedEventArgs e) => Interacted?.Invoke(this);

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

    private async void OnPathBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is not { } vm || IsWithinVolumeChip(e.Source as Visual))
            return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.CurrentPath);
    }

    private bool IsWithinVolumeChip(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, VolumeChip))
                return true;
        }

        return false;
    }

    private void OnVolumeChipClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        Interacted?.Invoke(this);
        vm.Drives.Refresh();
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

    private void OpenConnectDialog(PaneViewModel paneVm, StoredConnection? forEdit)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        OpenConnectDialogCore(mainVm, paneVm, forEdit, owner);
    }

    private static void OpenConnectDialogCore(MainViewModel mainVm, PaneViewModel paneVm, StoredConnection? forEdit, Window owner)
    {
        var dialogVm = BuildConnectDialog(mainVm, paneVm);
        if (forEdit is not null)
            dialogVm.ForEdit(forEdit);
        new ConnectWindow(dialogVm).ShowDialog(owner);
    }

    private static ConnectDialogViewModel BuildConnectDialog(MainViewModel mainVm, PaneViewModel paneVm)
    {
        var dialogVm = new ConnectDialogViewModel(
            mainVm.ConnectionManager,
            mainVm.ConnectionStore,
            mainVm.HostKeyStore,
            mainVm.Codec,
            mainVm.SmbConnectionManager,
            mainVm.SmbConnectionStore,
            mainVm.S3ConnectionManager,
            mainVm.S3ConnectionStore,
            mainVm.AzureConnectionManager,
            mainVm.AzureConnectionStore);

        dialogVm.Connected += info =>
        {
            paneVm.NavigateTo($"sftp://{info.Id}{info.InitialRemotePath}");
            mainVm.RebuildRemotePlaces();
        };

        dialogVm.SmbConnected += info =>
        {
            paneVm.NavigateTo($"smb://{info.Id}{info.InitialPath}");
            mainVm.RebuildRemotePlaces();
        };

        dialogVm.S3Connected += info =>
        {
            paneVm.NavigateTo($"s3://{info.Id}{info.InitialPath}");
            mainVm.RebuildRemotePlaces();
        };

        dialogVm.AzureConnected += info =>
        {
            paneVm.NavigateTo($"azure://{info.Id}{info.InitialPath}");
            mainVm.RebuildRemotePlaces();
        };

        return dialogVm;
    }

    private void RemoveConnection(string id)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var pane in new[] { mainVm.Left, mainVm.Right })
        {
            if (PathUtil.ParseRemote(pane.CurrentPath) is { } remote &&
                string.Equals(remote.Id, id, StringComparison.OrdinalIgnoreCase))
                pane.NavigateTo(home);
        }

        mainVm.ConnectionManager.Disconnect(id);
        var all = mainVm.ConnectionStore.Load().Where(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        mainVm.ConnectionStore.Save(all);
        mainVm.RebuildRemotePlaces();
    }

    private void ActivateShare(PaneViewModel paneVm, ShareRowViewModel share)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        if (share.IsSmb)
        {
            var smbStored = mainVm.SmbConnectionStore.Load()
                                .FirstOrDefault(c => string.Equals(c.Id, share.Id, StringComparison.OrdinalIgnoreCase))
                            ?? share.SmbStored;
            if (smbStored is null)
                return;

            mainVm.OpenSmbConnectDialog = (forEdit, targetPane) =>
                OpenSmbConnectDialogCore(mainVm, targetPane, forEdit, owner);
            mainVm.ConnectToSmbShare(smbStored, paneVm);
            return;
        }

        if (share.IsS3)
        {
            var s3Stored = mainVm.S3ConnectionStore.Load()
                               .FirstOrDefault(c => string.Equals(c.Id, share.Id, StringComparison.OrdinalIgnoreCase))
                           ?? share.S3Stored;
            if (s3Stored is null)
                return;

            mainVm.OpenS3ConnectDialog = (forEdit, targetPane) =>
                OpenS3ConnectDialogCore(mainVm, targetPane, forEdit, owner);
            mainVm.ConnectToS3Share(s3Stored, paneVm);
            return;
        }

        if (share.IsAzure)
        {
            var azureStored = mainVm.AzureConnectionStore.Load()
                                  .FirstOrDefault(c => string.Equals(c.Id, share.Id, StringComparison.OrdinalIgnoreCase))
                              ?? share.AzureStored;
            if (azureStored is null)
                return;

            mainVm.OpenAzureConnectDialog = (forEdit, targetPane) =>
                OpenAzureConnectDialogCore(mainVm, targetPane, forEdit, owner);
            mainVm.ConnectToAzureShare(azureStored, paneVm);
            return;
        }

        var stored = mainVm.ConnectionStore.Load()
                         .FirstOrDefault(c => string.Equals(c.Id, share.Id, StringComparison.OrdinalIgnoreCase))
                     ?? share.Stored;
        if (stored is null)
            return;

        mainVm.OpenConnectDialog = (forEdit, targetPane) =>
            OpenConnectDialogCore(mainVm, targetPane, forEdit, owner);

        mainVm.ConnectToShare(stored, paneVm);
    }

    private void DisconnectCurrentPane(PaneViewModel paneVm)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        if (PathUtil.ParseRemote(paneVm.CurrentPath) is not { } remote)
            return;

        if (string.Equals(remote.Scheme, "smb", StringComparison.OrdinalIgnoreCase))
            mainVm.SmbConnectionManager.Disconnect(remote.Id);
        else if (string.Equals(remote.Scheme, "s3", StringComparison.OrdinalIgnoreCase))
            mainVm.S3ConnectionManager.Disconnect(remote.Id);
        else if (string.Equals(remote.Scheme, "azure", StringComparison.OrdinalIgnoreCase))
            mainVm.AzureConnectionManager.Disconnect(remote.Id);
        else
            mainVm.ConnectionManager.Disconnect(remote.Id);

        paneVm.NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private void OpenSmbConnectDialog(PaneViewModel paneVm, StoredSmbConnection? forEdit)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        OpenSmbConnectDialogCore(mainVm, paneVm, forEdit, owner);
    }

    private static void OpenSmbConnectDialogCore(MainViewModel mainVm, PaneViewModel paneVm, StoredSmbConnection? forEdit, Window owner)
    {
        var dialogVm = BuildConnectDialog(mainVm, paneVm);
        if (forEdit is not null)
            dialogVm.ForEdit(forEdit);
        else
            dialogVm.Protocol = ConnectProtocol.Smb;
        new ConnectWindow(dialogVm).ShowDialog(owner);
    }

    private void RemoveSmbConnection(string id)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var pane in new[] { mainVm.Left, mainVm.Right })
        {
            if (PathUtil.ParseRemote(pane.CurrentPath) is { } remote
                && string.Equals(remote.Scheme, "smb", StringComparison.OrdinalIgnoreCase)
                && string.Equals(remote.Id, id, StringComparison.OrdinalIgnoreCase))
                pane.NavigateTo(home);
        }

        mainVm.SmbConnectionManager.Disconnect(id);
        var all = mainVm.SmbConnectionStore.Load()
            .Where(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        mainVm.SmbConnectionStore.Save(all);
        mainVm.RebuildRemotePlaces();
    }

    private void OpenS3ConnectDialog(PaneViewModel paneVm, StoredS3Connection? forEdit)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        OpenS3ConnectDialogCore(mainVm, paneVm, forEdit, owner);
    }

    private static void OpenS3ConnectDialogCore(MainViewModel mainVm, PaneViewModel paneVm, StoredS3Connection? forEdit, Window owner)
    {
        var dialogVm = BuildConnectDialog(mainVm, paneVm);
        if (forEdit is not null)
            dialogVm.ForEdit(forEdit);
        else
            dialogVm.Protocol = ConnectProtocol.S3;
        new ConnectWindow(dialogVm).ShowDialog(owner);
    }

    private void RemoveS3Connection(string id)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var pane in new[] { mainVm.Left, mainVm.Right })
        {
            if (PathUtil.ParseRemote(pane.CurrentPath) is { } remote
                && string.Equals(remote.Scheme, "s3", StringComparison.OrdinalIgnoreCase)
                && string.Equals(remote.Id, id, StringComparison.OrdinalIgnoreCase))
                pane.NavigateTo(home);
        }

        mainVm.S3ConnectionManager.Disconnect(id);
        var all = mainVm.S3ConnectionStore.Load()
            .Where(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        mainVm.S3ConnectionStore.Save(all);
        mainVm.RebuildRemotePlaces();
    }

    private void OpenAzureConnectDialog(PaneViewModel paneVm, StoredAzureConnection? forEdit)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        OpenAzureConnectDialogCore(mainVm, paneVm, forEdit, owner);
    }

    private static void OpenAzureConnectDialogCore(MainViewModel mainVm, PaneViewModel paneVm, StoredAzureConnection? forEdit, Window owner)
    {
        var dialogVm = BuildConnectDialog(mainVm, paneVm);
        if (forEdit is not null)
            dialogVm.ForEdit(forEdit);
        else
            dialogVm.Protocol = ConnectProtocol.AzureBlob;
        new ConnectWindow(dialogVm).ShowDialog(owner);
    }

    private void RemoveAzureConnection(string id)
    {
        HideDriveFlyout();
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            owner.DataContext is not MainViewModel mainVm)
            return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var pane in new[] { mainVm.Left, mainVm.Right })
        {
            if (PathUtil.ParseRemote(pane.CurrentPath) is { } remote
                && string.Equals(remote.Scheme, "azure", StringComparison.OrdinalIgnoreCase)
                && string.Equals(remote.Id, id, StringComparison.OrdinalIgnoreCase))
                pane.NavigateTo(home);
        }

        mainVm.AzureConnectionManager.Disconnect(id);
        var all = mainVm.AzureConnectionStore.Load()
            .Where(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        mainVm.AzureConnectionStore.Save(all);
        mainVm.RebuildRemotePlaces();
    }

    private void HideDriveFlyout()
    {
        VolumeChip.Flyout?.Hide();
        FocusList();
    }
}
