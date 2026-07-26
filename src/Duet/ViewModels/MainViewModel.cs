using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duet.Core.Operations;

namespace Duet.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    [ObservableProperty]
    private TransferViewModel? _activeTransfer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDirName))]
    private PaneViewModel _activePane;

    public PaneViewModel InactivePane => ActivePane == Left ? Right : Left;
    public string ActiveDirName => ActivePane.DirName;

    public MainViewModel(string leftPath, string rightPath)
    {
        Left = new PaneViewModel(leftPath);
        Right = new PaneViewModel(rightPath);
        _activePane = Left;
        Left.IsActive = true;

        Left.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaneViewModel.DirName) && ActivePane == Left)
                OnPropertyChanged(nameof(ActiveDirName));
        };
        Right.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaneViewModel.DirName) && ActivePane == Right)
                OnPropertyChanged(nameof(ActiveDirName));
        };
    }

    public MainViewModel()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    [RelayCommand]
    public void SwitchPane() => Activate(InactivePane);

    [RelayCommand]
    public void CopySelected() => StartTransfer(TransferMode.Copy);

    [RelayCommand]
    public void MoveSelected() => StartTransfer(TransferMode.Move);

    private void StartTransfer(TransferMode mode)
    {
        if (ActiveTransfer is not null && !ActiveTransfer.IsFinished)
            return;
        var source = ActivePane;
        var paths = source.SelectedRows.Select(r => r.Entry.FullPath).ToList();
        if (paths.Count == 0)
            return;

        ActiveTransfer?.Dispose();
        var session = TransferEngine.Start(paths, InactivePane.CurrentPath, mode);
        var transfer = new TransferViewModel(session, source);
        transfer.Dismissed += () =>
        {
            if (ActiveTransfer == transfer)
                ActiveTransfer = null;
            transfer.Dispose();
            Left.Reload(preserveSelection: true);
            Right.Reload(preserveSelection: true);
        };
        ActiveTransfer = transfer;
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        var rows = ActivePane.SelectedRows;
        foreach (var row in rows)
        {
            try
            {
                TrashService.Trash(row.Entry.FullPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
            }
        }

        if (rows.Count > 0)
            ActivePane.Reload(preserveSelection: false);
    }

    public void Activate(PaneViewModel pane)
    {
        ActivePane = pane;
        Left.IsActive = pane == Left;
        Right.IsActive = pane == Right;
        OnPropertyChanged(nameof(InactivePane));
    }

    public void Dispose()
    {
        Left.Dispose();
        Right.Dispose();
    }
}
