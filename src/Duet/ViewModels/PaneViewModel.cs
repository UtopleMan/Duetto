using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duet.Core.FileSystem;
using Duet.Core.Operations;

namespace Duet.ViewModels;

public partial class PaneViewModel : ObservableObject, IDisposable
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadDebounce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirName))]
    private string _currentPath;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(SizeHeader), nameof(TypeHeader), nameof(ModifiedHeader))]
    private SortColumn _sortColumn = SortColumn.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(SizeHeader), nameof(TypeHeader), nameof(ModifiedHeader))]
    private bool _sortAscending = true;

    public string NameHeader => Header("NAME", SortColumn.Name);
    public string SizeHeader => Header("SIZE", SortColumn.Size);
    public string TypeHeader => Header("TYPE", SortColumn.Type);
    public string ModifiedHeader => Header("MODIFIED", SortColumn.Modified);
    public string AccessHeader => OperatingSystem.IsWindows() ? "ACCESS" : "PERMS";

    private string Header(string label, SortColumn column) =>
        SortColumn == column ? label + (SortAscending ? " ▲" : " ▼") : label;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<FileRowViewModel> Rows { get; } = [];
    public SelectionModel<FileRowViewModel> Selection { get; } = new() { SingleSelect = false };

    /// <summary>Replaceable for tests; production launches with the OS default app.</summary>
    public Action<string> LaunchFile { get; set; } = static path =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public string DirName => Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
        ? name
        : CurrentPath;

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => Path.GetDirectoryName(CurrentPath) is not null;

    public PaneViewModel(string initialPath)
    {
        _currentPath = initialPath;
        Selection.Source = Rows;
        Selection.SelectionChanged += (_, _) => UpdateStatus();
        Reload(preserveSelection: false);
        StartWatcher();
    }

    [RelayCommand]
    public void NavigateTo(string path)
    {
        if (!Directory.Exists(path))
            return;
        _back.Push(CurrentPath);
        _forward.Clear();
        SetPath(path);
    }

    [RelayCommand]
    public void Back()
    {
        if (_back.Count == 0)
            return;
        _forward.Push(CurrentPath);
        SetPath(_back.Pop());
    }

    [RelayCommand]
    public void Forward()
    {
        if (_forward.Count == 0)
            return;
        _back.Push(CurrentPath);
        SetPath(_forward.Pop());
    }

    [RelayCommand]
    public void Up()
    {
        if (Path.GetDirectoryName(CurrentPath) is { } parent)
        {
            var cameFrom = Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
            NavigateTo(parent);
            if (cameFrom.Length > 0)
                SelectByName(cameFrom);
        }
    }

    public void Open(FileRowViewModel row)
    {
        if (row.IsParentNav)
            Up();
        else if (row.IsDirectory)
            NavigateTo(row.Entry.FullPath);
        else
            LaunchFile(row.Entry.FullPath);
    }

    public void OpenCursor()
    {
        if (Selection.SelectedItem is { } row)
            Open(row);
    }

    public void SortBy(SortColumn column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }

        Reload(preserveSelection: true);
    }

    public void Reload(bool preserveSelection)
    {
        var selectedNames = preserveSelection
            ? Selection.SelectedItems.OfType<FileRowViewModel>().Select(r => r.Name).ToHashSet()
            : [];

        List<FileEntry> entries;
        try
        {
            entries = EntrySorter.Sort(DirectoryLister.List(CurrentPath), SortColumn, SortAscending);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            entries = [];
        }

        Rows.Clear();
        if (Path.GetDirectoryName(CurrentPath) is { } parent)
            Rows.Add(FileRowViewModel.ParentNav(parent));
        foreach (var entry in entries)
            Rows.Add(new FileRowViewModel(entry));

        Selection.Clear();
        if (selectedNames.Count > 0)
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                if (selectedNames.Contains(Rows[i].Name))
                    Selection.Select(i);
            }
        }

        UpdateStatus();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    public IReadOnlyList<FileRowViewModel> SelectedRows =>
        Selection.SelectedItems.OfType<FileRowViewModel>().Where(r => !r.IsParentNav).ToList();

    public FileRowViewModel? StartRename()
    {
        if (Selection.SelectedItem is not { IsParentNav: false } row)
            return null;
        row.EditName = row.Name;
        row.IsEditing = true;
        return row;
    }

    public void CommitRename(FileRowViewModel row)
    {
        row.IsEditing = false;
        var newName = row.EditName.Trim();
        if (newName.Length == 0 || newName == row.Name)
            return;
        try
        {
            FileOps.Rename(row.Entry.FullPath, newName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        Reload(preserveSelection: false);
        SelectByName(newName);
    }

    public void CancelRename(FileRowViewModel row) => row.IsEditing = false;

    [RelayCommand]
    public void NewFolder()
    {
        try
        {
            var created = FileOps.NewFolder(CurrentPath);
            Reload(preserveSelection: false);
            SelectByName(Path.GetFileName(created));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Orthodox Insert-mark: toggles the cursor row's selection and moves the
    /// cursor down one. The ".." row is never marked, only stepped over.
    /// </summary>
    public void ToggleMarkAndAdvance()
    {
        if (Rows.Count == 0)
            return;
        var index = Math.Clamp(Selection.AnchorIndex, 0, Rows.Count - 1);
        if (!Rows[index].IsParentNav)
        {
            if (Selection.IsSelected(index))
                Selection.Deselect(index);
            else
                Selection.Select(index);
        }

        Selection.AnchorIndex = Math.Min(index + 1, Rows.Count - 1);
        UpdateStatus();
    }

    public void SelectByName(string name)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Name == name)
            {
                Selection.Clear();
                Selection.Select(i);
                return;
            }
        }
    }

    private void SetPath(string path)
    {
        CurrentPath = path;
        Reload(preserveSelection: false);
        if (Rows.Count > 0)
            Selection.Select(0);
        StartWatcher();
    }

    private void UpdateStatus()
    {
        var selected = Selection.SelectedItems.OfType<FileRowViewModel>().Where(r => !r.IsParentNav).ToList();
        var itemCount = Rows.Count(r => !r.IsParentNav);
        var text = itemCount == 1 ? "1 item" : $"{itemCount} items";
        if (selected.Count > 0)
        {
            var bytes = selected.Where(r => !r.IsDirectory).Sum(r => r.Entry.SizeBytes);
            var size = bytes > 0 ? $" — {FormatUtil.HumanSize(bytes)}" : "";
            text += $" · {selected.Count} selected{size}";
        }

        StatusText = text;
    }

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            _watcher = new FileSystemWatcher(CurrentPath)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler handler = (_, _) => ScheduleReload();
            _watcher.Created += handler;
            _watcher.Deleted += handler;
            _watcher.Changed += handler;
            _watcher.Renamed += (_, _) => ScheduleReload();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private void ScheduleReload() => Dispatcher.UIThread.Post(() =>
    {
        if (_reloadDebounce is null)
        {
            _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _reloadDebounce.Tick += OnDebounceTick;
        }

        _reloadDebounce.Stop();
        _reloadDebounce.Start();
    });

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _reloadDebounce!.Stop();
        Reload(preserveSelection: true);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _reloadDebounce?.Stop();
    }
}
