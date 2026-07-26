using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duet.Core.FileSystem;

namespace Duet.ViewModels;

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

    public string PaneSide { get; set; } = "left";
    public string HeaderText => $"Open in {PaneSide} pane";
    // instance property: {Binding ConnectShortcut} can't resolve statics
    public string ConnectShortcut => OperatingSystem.IsMacOS() ? "⌘K" : "Ctrl K";

    public ObservableCollection<VolumeRowViewModel> Volumes { get; } = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject), nameof(EjectLabel))]
    private VolumeInfo? _current;

    [ObservableProperty]
    private string _errorText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject))]
    private bool _isEjecting;

    public bool CanEject => Current is { IsEjectable: true } && !OperatingSystem.IsWindows() && !IsEjecting;
    public string EjectLabel => $"Eject {Current?.Name}";

    public event Action? CloseRequested;
    public event Action? ConnectRequested;

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

    public void OpenVolume(VolumeRowViewModel row)
    {
        _pane.NavigateTo(row.MountPath);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void Connect() => ConnectRequested?.Invoke();

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
