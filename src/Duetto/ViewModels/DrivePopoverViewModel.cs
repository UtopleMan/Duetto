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

/// <summary>
/// A row in the CONNECTED SHARES section of the drive popover.
/// Design tokens: connected dot #2f8f5b (Green resource), offline dot #c2bfb5 (TextHint),
/// offline status text #b08020 (SkipAmber).
/// </summary>
public sealed class ShareRowViewModel(StoredConnection stored, bool isConnected)
{
    public StoredConnection Stored { get; } = stored;
    public bool IsConnected { get; } = isConnected;
    public string Id => Stored.Id;
    public string Name => Stored.Name;
    public string Host => Stored.Host;
    public string InitialRemotePath => Stored.InitialRemotePath;

    /// <summary>Dot color: #2f8f5b when connected, #c2bfb5 when offline.</summary>
    public string DotColor => IsConnected ? "#2f8f5b" : "#c2bfb5";

    /// <summary>Status text: empty when connected, "Offline" in amber when not.</summary>
    public string StatusText => IsConnected ? "" : "Offline";

    /// <summary>Status text color: #b08020 (amber) for offline; invisible when connected.</summary>
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

    /// <summary>
    /// Returns the saved connections to display in the CONNECTED SHARES section.
    /// Seam: tests inject a fake that returns a fixed list.
    /// Default: no-op (empty), wired from MainViewModel after construction.
    /// </summary>
    public Func<StoredConnection[]> ListConnections { get; set; } = () => [];

    /// <summary>
    /// Returns true when the connection with the given id is live.
    /// Seam: tests inject a fake.
    /// Default: always false, wired from MainViewModel after construction.
    /// </summary>
    public Func<string, bool> IsConnected { get; set; } = _ => false;

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

    /// <summary>True when the pane is currently showing a remote path.</summary>
    public bool IsCurrentRemote => PathUtil.IsRemote(_pane.CurrentPath);

    /// <summary>
    /// True when the Disconnect row should be shown.
    /// Visible when the pane is navigated to a remote path.
    /// </summary>
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
            // Look up connection name from shares.
            var share = Shares.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            var name = share?.Name ?? id;
            return $"Disconnect {name}";
        }
    }

    public bool SharesSectionVisible => Shares.Count > 0;

    public event Action? CloseRequested;
    public event Action? ConnectRequested;

    /// <summary>
    /// Raised when the user wants to edit a saved connection.
    /// The argument is the stored connection to edit.
    /// </summary>
    public event Action<StoredConnection>? EditShareRequested;

    /// <summary>
    /// Raised when the user wants to remove a saved connection.
    /// The argument is the id of the connection to remove.
    /// </summary>
    public event Action<string>? RemoveShareRequested;

    /// <summary>
    /// Raised when the user clicks a share row to connect/navigate.
    /// Carries the share row as context so PaneView can decide what to do.
    /// </summary>
    public event Action<ShareRowViewModel>? ShareActivated;

    /// <summary>Raised when the user clicks Disconnect.</summary>
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

    /// <summary>
    /// Looks up the connection name for a given connection id from the saved connections seam.
    /// Returns null when the id is not found (caller falls back to showing the id).
    /// </summary>
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
        EditShareRequested?.Invoke(share.Stored);
    }

    public void RemoveShare(ShareRowViewModel share)
    {
        RemoveShareRequested?.Invoke(share.Id);
    }

    [RelayCommand]
    public void Connect() => ConnectRequested?.Invoke();

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
