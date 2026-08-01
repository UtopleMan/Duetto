using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.ViewModels;

public sealed class VolumeRowViewModel(VolumeInfo volume, bool isCurrent)
{
    public VolumeInfo Volume { get; } = volume;
    public bool IsCurrent { get; } = isCurrent;
    public string Name => Volume.Name;
    public string MountPath => Volume.MountPath;
    public string FreeText => $"{FormatUtil.HumanSize(Volume.FreeBytes)} free";
    public string SwatchColor => IsCurrent ? "#2f6fd0" : Volume.IsEjectable ? "#c8992f" : "#5b5950";
    public string BarColor => Volume.UsedPercent switch
    {
        > 90 => "#b8443c",
        > 75 => "#c07a3a",
        _ => "#2f8f5b",
    };
    public double BarWidth => Volume.UsedPercent * 1.7; // track is 170px wide
    public string RowBg => IsCurrent ? "#eef1f7" : "Transparent";
}

// One row in the popover's "Connected Shares" list. Scheme-tagged so a single list can merge
// SFTP and SMB connections drawn from two separate stores; exactly one of Stored / SmbStored is
// set. The view routes activate/edit/remove by Scheme.
public sealed class ShareRowViewModel
{
    public string Scheme { get; }

    public StoredConnection? Stored { get; }

    public StoredSmbConnection? SmbStored { get; }

    public bool IsConnected { get; }

    public ShareRowViewModel(StoredConnection stored, bool isConnected)
    {
        Scheme = "sftp";
        Stored = stored;
        IsConnected = isConnected;
        Id = stored.Id;
        Name = stored.Name;
        Host = stored.Host;
        InitialRemotePath = stored.InitialRemotePath;
    }

    public ShareRowViewModel(StoredSmbConnection stored, bool isConnected)
    {
        Scheme = "smb";
        SmbStored = stored;
        IsConnected = isConnected;
        Id = stored.Id;
        Name = stored.Name;
        Host = stored.Host;
        InitialRemotePath = stored.InitialPath;
    }

    public string Id { get; }
    public string Name { get; }
    public string Host { get; }
    public string InitialRemotePath { get; }

    public bool IsSmb => Scheme == "smb";

    public string SchemeLabel => IsSmb ? "SMB" : "SFTP";

    public string DotColor => IsConnected ? "#2f8f5b" : "#c2bfb5";

    public string StatusText => IsConnected ? "" : "Offline";

    public string StatusTextColor => "#b08020";

    public bool StatusTextVisible => !IsConnected;
}

public partial class DrivePopoverViewModel : ObservableObject
{
    private readonly PaneViewModel _pane;
    private IReadOnlyList<VolumeInfo> _all = [];
    private bool _loaded;

    public DrivePopoverViewModel(PaneViewModel pane)
    {
        _pane = pane;
    }

    public Func<IReadOnlyList<VolumeInfo>> ListVolumes { get; set; } = VolumeCatalog.List;
    public Func<string, Task<EjectResult>> Eject { get; set; } = m => VolumeEjector.EjectAsync(m);

    // Test/wiring seam: defaults to empty, replaced by MainViewModel after construction.
    public Func<StoredConnection[]> ListConnections { get; set; } = () => [];

    // Test/wiring seam: defaults to false, replaced by MainViewModel after construction.
    public Func<string, bool> IsConnected { get; set; } = _ => false;

    // SMB counterparts of the two seams above; merged into the same Shares list.
    public Func<StoredSmbConnection[]> ListSmbConnections { get; set; } = () => [];

    public Func<string, bool> IsSmbConnected { get; set; } = _ => false;

    public string PaneSide { get; set; } = "left";
    public string HeaderText => $"Open in {PaneSide} pane";
    // instance property: {Binding ConnectShortcut} can't resolve statics
    public string ConnectShortcut => OperatingSystem.IsMacOS() ? "⌘K" : "Ctrl K";

    public ObservableCollection<VolumeRowViewModel> Volumes { get; } = [];
    public ObservableCollection<ShareRowViewModel> Shares { get; } = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject), nameof(EjectRowVisible), nameof(EjectLabel))]
    private VolumeInfo? _current;

    [ObservableProperty]
    private string _errorText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject), nameof(EjectRowVisible))]
    private bool _isEjecting;

    // True when the current volume is ejectable and the platform supports eject.
    // Controls row *visibility* — the row stays in the layout while ejecting.
    public bool EjectRowVisible => Current is { IsEjectable: true } && !OperatingSystem.IsWindows();

    // True when ejection is allowed right now (visible and not already in progress).
    // Controls row *enabled* state — the row is disabled while an eject is running.
    public bool CanEject => EjectRowVisible && !IsEjecting;
    public string EjectLabel => $"Eject {Current?.Name}";

    public bool IsCurrentRemote => PathUtil.IsRemote(_pane.CurrentPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisconnectLabel))]
    private bool _disconnectRowVisible;

    public string DisconnectLabel
    {
        get
        {
            if (!PathUtil.IsRemote(_pane.CurrentPath))
                return "Disconnect";
            var id = PathUtil.ParseRemote(_pane.CurrentPath)?.Id ?? "";
            var share = Shares.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            var name = share?.Name ?? id;
            return $"Disconnect {name}";
        }
    }

    public bool SharesSectionVisible => Shares.Count > 0;

    public event Action? CloseRequested;
    public event Action? ConnectRequested;

    // SMB "new connection" entry point (the second Connect button).
    public event Action? ConnectSmbRequested;

    public event Action<StoredConnection>? EditShareRequested;

    public event Action<StoredSmbConnection>? EditSmbShareRequested;

    public event Action<string>? RemoveShareRequested;

    public event Action<string>? RemoveSmbShareRequested;

    public event Action<ShareRowViewModel>? ShareActivated;

    public event Action? DisconnectRequested;

    partial void OnFilterTextChanged(string value) => RebuildRows();

    public void Refresh()
    {
        _all = ListVolumes();
        _loaded = true;
        Current = VolumeCatalog.FindByPath(_all, _pane.CurrentPath);
        ErrorText = "";
        // Reset the backing field directly: the property setter's change callback
        // would run RebuildRows a second time on this popover-open hot path.
        _filterText = "";
        OnPropertyChanged(nameof(FilterText));
        RebuildShareRows();
        DisconnectRowVisible = PathUtil.IsRemote(_pane.CurrentPath);
        RebuildRows();
    }

    public VolumeInfo? VolumeFor(string path)
    {
        if (!_loaded)
        {
            _all = ListVolumes();
            _loaded = true;
        }

        return VolumeCatalog.FindByPath(_all, path);
    }

    public string? ConnectionNameFor(string id)
    {
        foreach (var stored in ListConnections())
        {
            if (string.Equals(stored.Id, id, StringComparison.OrdinalIgnoreCase))
                return stored.Name;
        }
        return null;
    }

    public void OpenVolume(VolumeRowViewModel row)
    {
        _pane.NavigateTo(row.MountPath);
        CloseRequested?.Invoke();
    }

    public void ActivateShare(ShareRowViewModel share)
    {
        ShareActivated?.Invoke(share);
    }

    public void EditShare(ShareRowViewModel share)
    {
        if (share.IsSmb)
            EditSmbShareRequested?.Invoke(share.SmbStored!);
        else
            EditShareRequested?.Invoke(share.Stored!);
    }

    public void RemoveShare(ShareRowViewModel share)
    {
        if (share.IsSmb)
            RemoveSmbShareRequested?.Invoke(share.Id);
        else
            RemoveShareRequested?.Invoke(share.Id);
    }

    [RelayCommand]
    public void Connect() => ConnectRequested?.Invoke();

    [RelayCommand]
    public void ConnectSmb() => ConnectSmbRequested?.Invoke();

    [RelayCommand]
    public void Disconnect() => DisconnectRequested?.Invoke();

    [RelayCommand]
    public async Task EjectCurrentAsync()
    {
        if (Current is not { IsEjectable: true } volume || IsEjecting)
            return;
        IsEjecting = true;
        ErrorText = "";
        try
        {
            var result = await Eject(volume.MountPath);
            if (!result.Success)
            {
                ErrorText = result.Error;
                return;
            }

            if (VolumeCatalog.FindByPath([volume], _pane.CurrentPath) is not null)
                _pane.NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            CloseRequested?.Invoke();
        }
        finally
        {
            IsEjecting = false;
        }
    }

    private void RebuildShareRows()
    {
        Shares.Clear();
        foreach (var stored in ListConnections())
            Shares.Add(new ShareRowViewModel(stored, IsConnected(stored.Id)));
        foreach (var stored in ListSmbConnections())
            Shares.Add(new ShareRowViewModel(stored, IsSmbConnected(stored.Id)));
        OnPropertyChanged(nameof(SharesSectionVisible));
        OnPropertyChanged(nameof(DisconnectLabel));
    }

    private void RebuildRows()
    {
        Volumes.Clear();
        foreach (var v in _all)
        {
            if (FilterText.Length > 0
                && !v.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                && !v.MountPath.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                continue;
            Volumes.Add(new VolumeRowViewModel(v, v.MountPath == Current?.MountPath));
        }
    }
}
