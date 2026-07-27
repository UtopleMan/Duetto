using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;

namespace Duetto.ViewModels;

public partial class PaneViewModel : ObservableObject, IDisposable
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadDebounce;
    private CancellationTokenSource? _loadCts;

    /// <summary>Reads a directory. Seam for tests; production lists the real filesystem.</summary>
    public Func<string, IReadOnlyList<FileEntry>> Lister { get; set; } = DirectoryLister.List;

    /// <summary>
    /// Schedules the listing work. Default runs it inline (synchronous); production
    /// swaps in <see cref="BackgroundScheduler"/> so slow directories never block the UI.
    /// </summary>
    public Func<Func<IReadOnlyList<FileEntry>>, CancellationToken, Task<IReadOnlyList<FileEntry>>> LoadScheduler { get; set; }
        = static (work, _) => Task.FromResult(work());

    /// <summary>Runs the listing on a thread-pool thread. Wired in by production composition.</summary>
    public static readonly Func<Func<IReadOnlyList<FileEntry>>, CancellationToken, Task<IReadOnlyList<FileEntry>>>
        BackgroundScheduler = static (work, ct) => Task.Run(work, ct);

    /// <summary>Completes when the in-flight load finishes; tests await this to settle.</summary>
    public Task LoadCompletion { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirName), nameof(VolumeChipText), nameof(PathTailText))]
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

    /// <summary>True while a background listing is in flight (drives the "Loading…" overlay).</summary>
    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<FileRowViewModel> Rows { get; } = [];

    /// <summary>The cursor: exactly one row. Marks live on <see cref="FileRowViewModel.IsMarked"/>.</summary>
    public SelectionModel<FileRowViewModel> Selection { get; } = new() { SingleSelect = true };

    /// <summary>Raised after Reload rebuilds Rows — the view restores keyboard focus.</summary>
    public event Action? Reloaded;

    /// <summary>Replaceable for tests; production launches with the OS default app.</summary>
    public Action<string> LaunchFile { get; set; } = static path =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public string DirName => Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
        ? name
        : CurrentPath;

    public string VolumeChipText
    {
        get
        {
            if (Drives.VolumeFor(CurrentPath) is not { } volume)
                return CurrentPath;
            return OperatingSystem.IsWindows() ? $"{volume.MountPath} {volume.Name}" : volume.Name;
        }
    }

    public string PathTailText
    {
        get
        {
            if (Drives.VolumeFor(CurrentPath) is not { } volume)
                return "";
            var mount = volume.MountPath.TrimEnd('/', '\\');
            var trimmedCurrent = CurrentPath.TrimEnd('/', '\\');
            return trimmedCurrent.Length > mount.Length ? CurrentPath[mount.Length..] : "";
        }
    }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => Path.GetDirectoryName(CurrentPath) is not null;

    public DrivePopoverViewModel Drives { get; }

    public PaneViewModel(string initialPath)
    {
        _currentPath = initialPath;
        Drives = new DrivePopoverViewModel(this);
        Selection.Source = Rows;
        Selection.SelectionChanged += (_, _) => UpdateStatus();
        Reload(preserveSelection: false);
        StartWatcher();
    }

    [RelayCommand]
    public void NavigateTo(string path) => NavigateTo(path, null);

    /// <summary>Navigate, then select <paramref name="selectName"/> once the load lands (else the first row).</summary>
    public void NavigateTo(string path, string? selectName)
    {
        if (!Directory.Exists(path))
            return;
        _back.Push(CurrentPath);
        _forward.Clear();
        SetPath(path, selectName);
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
            NavigateTo(parent, cameFrom.Length > 0 ? cameFrom : null);
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

    public Task Reload(bool preserveSelection) =>
        StartLoad(preserveSelection, selectAfter: null, selectFirst: false);

    /// <summary>
    /// Kicks off a listing via <see cref="LoadScheduler"/>. A newer load cancels
    /// and supersedes any in-flight one; the stale result is discarded so rapid
    /// navigation always lands on the final directory.
    /// </summary>
    private Task StartLoad(bool preserveSelection, string? selectAfter, bool selectFirst)
    {
        var markedNames = preserveSelection
            ? Rows.Where(r => r.IsMarked).Select(r => r.Name).ToHashSet()
            : [];
        var cursorName = preserveSelection ? CursorRow?.Name : null;

        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var token = cts.Token;
        var path = CurrentPath;
        var sortColumn = SortColumn;
        var ascending = SortAscending;

        Func<IReadOnlyList<FileEntry>> work = () =>
        {
            try
            {
                return EntrySorter.Sort(Lister(path), sortColumn, ascending);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                return [];
            }
        };

        Task<IReadOnlyList<FileEntry>> task;
        try
        {
            task = LoadScheduler(work, token);
        }
        catch (OperationCanceledException)
        {
            return Task.CompletedTask;
        }

        if (!task.IsCompleted)
            IsLoading = true;

        return LoadCompletion = ApplyWhenReady(task, cts, markedNames, cursorName, selectAfter, selectFirst);
    }

    private async Task ApplyWhenReady(
        Task<IReadOnlyList<FileEntry>> task, CancellationTokenSource cts,
        HashSet<string> markedNames, string? cursorName, string? selectAfter, bool selectFirst)
    {
        IReadOnlyList<FileEntry> entries;
        try
        {
            entries = await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A newer load superseded this one while it was in flight — discard the result.
        if (!ReferenceEquals(cts, _loadCts))
            return;

        ApplyRows(entries, markedNames, cursorName, selectAfter, selectFirst);
        IsLoading = false;
    }

    private void ApplyRows(
        IReadOnlyList<FileEntry> entries, HashSet<string> markedNames,
        string? cursorName, string? selectAfter, bool selectFirst)
    {
        Rows.Clear();
        if (Path.GetDirectoryName(CurrentPath) is { } parent)
            Rows.Add(FileRowViewModel.ParentNav(parent));
        foreach (var entry in entries)
            Rows.Add(new FileRowViewModel(entry));

        Selection.Clear();
        for (var i = 0; i < Rows.Count; i++)
        {
            if (markedNames.Contains(Rows[i].Name))
                Rows[i].IsMarked = true;
            if (cursorName is not null && Rows[i].Name == cursorName)
                Selection.Select(i);
        }

        if (selectAfter is not null)
            SelectByName(selectAfter);
        else if (selectFirst && Rows.Count > 0)
            Selection.Select(0);

        UpdateStatus();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        Reloaded?.Invoke();
    }

    public FileRowViewModel? CursorRow => Selection.SelectedItem;
    public bool HasMarks => Rows.Any(r => r.IsMarked);

    /// <summary>Operation targets: the marked rows, or the cursor row when nothing is marked.</summary>
    public IReadOnlyList<FileRowViewModel> SelectedRows
    {
        get
        {
            var marked = Rows.Where(r => r.IsMarked && !r.IsParentNav).ToList();
            if (marked.Count > 0)
                return marked;
            return CursorRow is { IsParentNav: false } cursor ? [cursor] : [];
        }
    }

    public void ToggleMarkAt(FileRowViewModel row)
    {
        if (row.IsParentNav)
            return;
        row.IsMarked = !row.IsMarked;
        var index = Rows.IndexOf(row);
        if (index >= 0)
            Selection.Select(index);
        UpdateStatus();
    }

    /// <summary>Shift-click: marks every row between the cursor and the target, cursor moves to target.</summary>
    public void MarkRangeTo(FileRowViewModel row)
    {
        var to = Rows.IndexOf(row);
        if (to < 0)
            return;
        var from = Math.Max(0, Selection.SelectedIndex);
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            if (!Rows[i].IsParentNav)
                Rows[i].IsMarked = true;
        }

        Selection.Select(to);
        UpdateStatus();
    }

    /// <summary>Shift+arrow: toggles the cursor row's mark, then moves the cursor.</summary>
    public void MarkCursorAndMove(int delta)
    {
        if (Rows.Count == 0)
            return;
        var index = Math.Clamp(Selection.SelectedIndex, 0, Rows.Count - 1);
        if (!Rows[index].IsParentNav)
            Rows[index].IsMarked = !Rows[index].IsMarked;
        Selection.Select(Math.Clamp(index + delta, 0, Rows.Count - 1));
        UpdateStatus();
    }

    public void ClearMarks()
    {
        foreach (var row in Rows)
            row.IsMarked = false;
        UpdateStatus();
    }

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

        StartLoad(preserveSelection: false, selectAfter: newName, selectFirst: false);
    }

    public void CancelRename(FileRowViewModel row) => row.IsEditing = false;

    [RelayCommand]
    public void NewFolder()
    {
        try
        {
            var created = FileOps.NewFolder(CurrentPath);
            StartLoad(preserveSelection: false, selectAfter: Path.GetFileName(created), selectFirst: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Orthodox Insert-mark: toggles the cursor row's mark and moves the cursor
    /// down one. The ".." row is never marked, only stepped over.
    /// </summary>
    public void ToggleMarkAndAdvance()
    {
        if (Rows.Count == 0)
            return;
        var index = Math.Clamp(Selection.SelectedIndex, 0, Rows.Count - 1);
        if (!Rows[index].IsParentNav)
            Rows[index].IsMarked = !Rows[index].IsMarked;
        Selection.Select(Math.Min(index + 1, Rows.Count - 1));
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

    private void SetPath(string path, string? selectName = null)
    {
        CurrentPath = path;
        StartWatcher();
        StartLoad(preserveSelection: false, selectAfter: selectName, selectFirst: selectName is null);
    }

    private void UpdateStatus()
    {
        var marked = Rows.Where(r => r.IsMarked && !r.IsParentNav).ToList();
        var itemCount = Rows.Count(r => !r.IsParentNav);
        var text = itemCount == 1 ? "1 item" : $"{itemCount} items";
        if (marked.Count > 0)
        {
            var bytes = marked.Where(r => !r.IsDirectory).Sum(r => r.Entry.SizeBytes);
            var size = bytes > 0 ? $" — {FormatUtil.HumanSize(bytes)}" : "";
            text += $" · {marked.Count} selected{size}";
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
        _loadCts?.Cancel();
        _watcher?.Dispose();
        _reloadDebounce?.Stop();
    }
}
