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
    public string SwatchColor => IsCurrent
        ? PaletteLookup.Hex("Accent", "#2f6fd0")
        : Volume.IsEjectable ? PaletteLookup.Hex("FolderMark", "#c8992f") : PaletteLookup.Hex("TextMid", "#5b5950");
    public string BarColor => Volume.UsedPercent switch
    {
        > 90 => "#b8443c",
        > 75 => "#c07a3a",
        _ => "#2f8f5b",
    };
    public double BarWidth => Volume.UsedPercent * 1.7;
    public string RowBg => IsCurrent ? PaletteLookup.Hex("ChipBg", "#eef1f7") : "Transparent";
}

public sealed class ShareRowViewModel
{
    public string Scheme { get; }

    public StoredConnection? Stored { get; }

    public StoredSmbConnection? SmbStored { get; }

    public StoredS3Connection? S3Stored { get; }

    public StoredAzureConnection? AzureStored { get; }

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

    public ShareRowViewModel(StoredS3Connection stored, bool isConnected)
    {
        Scheme = "s3";
        S3Stored = stored;
        IsConnected = isConnected;
        Id = stored.Id;
        Name = stored.Name;
        Host = string.IsNullOrEmpty(stored.Endpoint) ? "AWS" : stored.Endpoint;
        InitialRemotePath = stored.InitialPath;
    }

    public ShareRowViewModel(StoredAzureConnection stored, bool isConnected)
    {
        Scheme = "azure";
        AzureStored = stored;
        IsConnected = isConnected;
        Id = stored.Id;
        Name = stored.Name;
        Host = !string.IsNullOrEmpty(stored.AccountName) ? stored.AccountName
            : (string.IsNullOrEmpty(stored.Endpoint) ? "Azure" : stored.Endpoint);
        InitialRemotePath = stored.InitialPath;
    }

    public string Id { get; }
    public string Name { get; }
    public string Host { get; }
    public string InitialRemotePath { get; }

    public bool IsSmb => Scheme == "smb";

    public bool IsS3 => Scheme == "s3";

    public bool IsAzure => Scheme == "azure";

    public string SchemeLabel => Scheme switch { "smb" => "SMB", "s3" => "S3", "azure" => "Azure", _ => "SFTP" };

    public string DotColor => IsConnected
        ? PaletteLookup.Hex("Green", "#2f8f5b")
        : PaletteLookup.Hex("TextHint", "#c2bfb5");

    public string StatusText => IsConnected ? "" : "Offline";

    public string StatusTextColor => PaletteLookup.Hex("SkipAmber", "#b08020");

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

    public Func<StoredConnection[]> ListConnections { get; set; } = () => [];

    public Func<string, bool> IsConnected { get; set; } = _ => false;

    public Func<StoredSmbConnection[]> ListSmbConnections { get; set; } = () => [];

    public Func<string, bool> IsSmbConnected { get; set; } = _ => false;

    public Func<StoredS3Connection[]> ListS3Connections { get; set; } = () => [];

    public Func<string, bool> IsS3Connected { get; set; } = _ => false;

    public Func<StoredAzureConnection[]> ListAzureConnections { get; set; } = () => [];

    public Func<string, bool> IsAzureConnected { get; set; } = _ => false;

    public string PaneSide { get; set; } = "left";
    public string HeaderText => $"Open in {PaneSide} pane";
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

    public bool EjectRowVisible => Current is { IsEjectable: true } && !OperatingSystem.IsWindows();

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

    public event Action<StoredConnection>? EditShareRequested;

    public event Action<StoredSmbConnection>? EditSmbShareRequested;

    public event Action<StoredS3Connection>? EditS3ShareRequested;

    public event Action<StoredAzureConnection>? EditAzureShareRequested;

    public event Action<string>? RemoveShareRequested;

    public event Action<string>? RemoveSmbShareRequested;

    public event Action<string>? RemoveS3ShareRequested;

    public event Action<string>? RemoveAzureShareRequested;

    public event Action<ShareRowViewModel>? ShareActivated;

    public event Action? DisconnectRequested;

    partial void OnFilterTextChanged(string value) => RebuildRows();

    public void Refresh()
    {
        _all = ListVolumes();
        _loaded = true;
        Current = VolumeCatalog.FindByPath(_all, _pane.CurrentPath);
        ErrorText = "";
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
        else if (share.IsS3)
            EditS3ShareRequested?.Invoke(share.S3Stored!);
        else if (share.IsAzure)
            EditAzureShareRequested?.Invoke(share.AzureStored!);
        else
            EditShareRequested?.Invoke(share.Stored!);
    }

    public void RemoveShare(ShareRowViewModel share)
    {
        if (share.IsSmb)
            RemoveSmbShareRequested?.Invoke(share.Id);
        else if (share.IsS3)
            RemoveS3ShareRequested?.Invoke(share.Id);
        else if (share.IsAzure)
            RemoveAzureShareRequested?.Invoke(share.Id);
        else
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
        foreach (var stored in ListSmbConnections())
            Shares.Add(new ShareRowViewModel(stored, IsSmbConnected(stored.Id)));
        foreach (var stored in ListS3Connections())
            Shares.Add(new ShareRowViewModel(stored, IsS3Connected(stored.Id)));
        foreach (var stored in ListAzureConnections())
            Shares.Add(new ShareRowViewModel(stored, IsAzureConnected(stored.Id)));
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
