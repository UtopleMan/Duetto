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

    public CommandBarViewModel CommandBar { get; }
    public SearchViewModel Search { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDirName))]
    private PaneViewModel _activePane;

    public PaneViewModel InactivePane => ActivePane == Left ? Right : Left;
    public string ActiveDirName => ActivePane.DirName;
    public static string SearchHint => OperatingSystem.IsMacOS() ? "⌘F" : "Ctrl F";

    public MainViewModel(string leftPath, string rightPath)
    {
        Left = new PaneViewModel(leftPath);
        Right = new PaneViewModel(rightPath);
        _activePane = Left;
        Left.IsActive = true;
        CommandBar = new CommandBarViewModel(() => ActivePane.CurrentPath);
        CommandBar.CommandFinished += () =>
        {
            Left.Reload(preserveSelection: true);
            Right.Reload(preserveSelection: true);
        };
        Search = new SearchViewModel(() => ActivePane.CurrentPath);
        Search.RevealRequested += entry =>
        {
            if (Path.GetDirectoryName(entry.FullPath) is { } dir)
            {
                Left.NavigateTo(dir);
                Left.SelectByName(entry.Name);
                Activate(Left);
            }
        };

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
        // Search results transfer into the left pane's dir; pane selections into the other pane.
        if (Search.IsActive)
        {
            var entries = Search.SelectedEntries;
            StartTransfer(entries.Select(e => e.FullPath).ToList(), Left.CurrentPath, mode, sourcePane: null);
            return;
        }

        var source = ActivePane;
        var paths = source.SelectedRows.Select(r => r.Entry.FullPath).ToList();
        StartTransfer(paths, InactivePane.CurrentPath, mode, source);
    }

    private void StartTransfer(IReadOnlyList<string> paths, string destinationDir, TransferMode mode, PaneViewModel? sourcePane)
    {
        if (paths.Count == 0 || (ActiveTransfer is not null && !ActiveTransfer.IsFinished))
            return;

        ActiveTransfer?.Dispose();
        var session = TransferEngine.Start(paths, destinationDir, mode);
        var transfer = new TransferViewModel(session, sourcePane);
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
        if (Search.IsActive)
        {
            var entries = Search.SelectedEntries;
            foreach (var entry in entries)
                TryTrash(entry.FullPath);
            if (entries.Count > 0)
            {
                foreach (var row in Search.Results.Where(r => entries.Contains(r.Entry)).ToList())
                    Search.Results.Remove(row);
                Left.Reload(preserveSelection: true);
                Right.Reload(preserveSelection: true);
            }

            return;
        }

        var rows = ActivePane.SelectedRows;
        foreach (var row in rows)
            TryTrash(row.Entry.FullPath);
        if (rows.Count > 0)
            ActivePane.Reload(preserveSelection: false);
    }

    private static void TryTrash(string path)
    {
        try
        {
            TrashService.Trash(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
        }
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
