using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Core.Remote;
using Renci.SshNet.Common;

namespace Duetto.ViewModels;

public partial class PaneViewModel : ObservableObject, IDisposable
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadDebounce;
    private CancellationTokenSource? _loadCts;

    /// <summary>The active "new folder/file" placeholder row, or null when none is being named.</summary>
    private FileRowViewModel? _editingPlaceholder;

    /// <summary>
    /// Maps a path to its provider. Default is a shared local-only instance; tests inject a
    /// registry pre-populated with an in-memory provider to exercise remote navigation without
    /// touching the disk. Matches the pattern used by <see cref="MainViewModel.Registry"/>.
    /// </summary>
    public FileSystemRegistry Registry { get; set; } = SharedLocalRegistry;

    /// <summary>A local-only registry shared by all panes that have not been given a custom one.</summary>
    private static readonly FileSystemRegistry SharedLocalRegistry = new();

    /// <summary>
    /// Reads a directory. Routes through <see cref="Registry"/> so remote addresses
    /// resolve to the correct provider. Seam for tests: inject a custom registry or
    /// replace this delegate directly for lower-level overrides.
    /// </summary>
    public Func<string, IReadOnlyList<FileEntry>> Lister { get; set; }

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

    /// <summary>Schedules the rename move. Default inline; production offloads to a worker thread.</summary>
    public Func<Action, Task> RenameScheduler { get; set; }
        = static work => { work(); return Task.CompletedTask; };

    /// <summary>Runs the rename move on a thread-pool thread. Wired in by production composition.</summary>
    public static readonly Func<Action, Task> BackgroundRenameScheduler = static work => Task.Run(work);

    /// <summary>Completes when the in-flight rename finishes; tests await this to settle.</summary>
    public Task RenameCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>True when a FileSystemWatcher is live for the current path. Remote panes never have one.</summary>
    public bool HasActiveWatcher => _watcher is not null;

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

    public string DirName => PathUtil.Leaf(CurrentPath) is { Length: > 0 } name ? name : CurrentPath;

    public string VolumeChipText
    {
        get
        {
            // Remote path: show the connection name (looked up by id from the shares seam).
            if (PathUtil.ParseRemote(CurrentPath) is { } remote)
            {
                var name = Drives.ConnectionNameFor(remote.Id);
                return name ?? remote.Id;
            }

            if (Drives.VolumeFor(CurrentPath) is not { } volume)
                return CurrentPath;
            return OperatingSystem.IsWindows() ? $"{volume.MountPath} {volume.Name}" : volume.Name;
        }
    }

    public string PathTailText
    {
        get
        {
            // Remote path: the tail is the provider-local path from PathUtil.
            if (PathUtil.ParseRemote(CurrentPath) is { } remote)
            {
                var local = remote.LocalPath;
                return local == "/" ? "" : local;
            }

            if (Drives.VolumeFor(CurrentPath) is not { } volume)
                return "";
            var mount = volume.MountPath.TrimEnd('/', '\\');
            var trimmedCurrent = CurrentPath.TrimEnd('/', '\\');
            return trimmedCurrent.Length > mount.Length ? CurrentPath[mount.Length..] : "";
        }
    }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => PathUtil.Parent(CurrentPath) is not null;

    public DrivePopoverViewModel Drives { get; }

    public PaneViewModel(string initialPath, FileSystemRegistry? registry = null)
    {
        _currentPath = initialPath;
        if (registry is not null)
            Registry = registry;
        // Default lister routes through the registry so remote addresses hit the right provider.
        // Tests can replace the Lister delegate directly for lower-level overrides.
        Lister = path =>
        {
            var (provider, localPath) = Registry.Resolve(path);
            return provider.List(localPath);
        };
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
        try
        {
            // For remote addresses, skip the synchronous pre-check — it would stall the UI
            // thread on a network call. The already-guarded background load handles a bad path.
            if (!PathUtil.IsRemote(path))
            {
                var (provider, localPath) = Registry.Resolve(path);
                if (!provider.DirectoryExists(localPath))
                    return;
            }
            else
            {
                // Validate that the address resolves to a registered provider; if not, bail.
                Registry.Resolve(path);
            }
        }
        catch (Exception e) when (e is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or SshException
            or InvalidOperationException
            or HostKeyChangedException)
        {
            return;
        }

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
        if (PathUtil.Parent(CurrentPath) is { } parent)
        {
            var cameFrom = PathUtil.Leaf(CurrentPath);
            NavigateTo(parent, cameFrom.Length > 0 ? cameFrom : null);
        }
    }

    public void Open(FileRowViewModel row)
    {
        if (row.IsParentNav)
            Up();
        else if (row.IsDirectory)
            NavigateTo(PathUtil.ToAddress(CurrentPath, row.Entry.FullPath));
        else
        {
            // Remote file open / download-and-open is a deferred feature — no-op until it ships.
            if (PathUtil.IsRemote(CurrentPath))
                return;
            LaunchFile(row.Entry.FullPath);
        }
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
        var cursorIndex = preserveSelection ? Selection.SelectedIndex : -1;

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
            catch (Exception e) when (e is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException
                or SshException
                or InvalidOperationException
                or HostKeyChangedException)
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

        return LoadCompletion = ApplyWhenReady(task, cts, markedNames, cursorName, cursorIndex, selectAfter, selectFirst);
    }

    private async Task ApplyWhenReady(
        Task<IReadOnlyList<FileEntry>> task, CancellationTokenSource cts,
        HashSet<string> markedNames, string? cursorName, int cursorIndex, string? selectAfter, bool selectFirst)
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

        ApplyRows(entries, markedNames, cursorName, cursorIndex, selectAfter, selectFirst);
        IsLoading = false;
    }

    private void ApplyRows(
        IReadOnlyList<FileEntry> entries, HashSet<string> markedNames,
        string? cursorName, int cursorIndex, string? selectAfter, bool selectFirst)
    {
        Rows.Clear();
        if (PathUtil.Parent(CurrentPath) is { } parent)
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

        // The preserved cursor row is gone (deleted, or renamed/moved away): land on whatever
        // now occupies its slot — the next item, or the last row when it was the tail. Leaving
        // no selection would strand keyboard focus (no row container to focus).
        if (cursorName is not null && Selection.SelectedIndex < 0 && Rows.Count > 0)
            Selection.Select(Math.Clamp(cursorIndex, 0, Rows.Count - 1));

        // An in-progress new-entry placeholder is synthetic (not on disk), so a rebuild would
        // drop it — re-attach it in edit mode so an unrelated reload can't cancel the naming.
        if (_editingPlaceholder is { } placeholder)
        {
            var insertAt = Rows.Count > 0 && Rows[0].IsParentNav ? 1 : 0;
            Rows.Insert(insertAt, placeholder);
            Selection.Select(insertAt);
        }

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
        // Capability gate: no-op when the provider doesn't support rename.
        var (provider, _) = Registry.Resolve(CurrentPath);
        if (!provider.Capabilities.CanRename)
            return null;
        row.EditName = row.Name;
        row.IsEditing = true;
        return row;
    }

    /// <summary>Enter / programmatic commit. A colliding placeholder name stays in edit mode.</summary>
    public void CommitRename(FileRowViewModel row)
    {
        if (row.IsNewPlaceholder)
        {
            CommitNewEntry(row, fromBlur: false);
            return;
        }

        row.IsEditing = false;
        var newName = row.EditName.Trim();
        if (newName.Length == 0 || newName == row.Name)
            return;

        RenameCompletion = RunRenameAsync(row.Entry.FullPath, newName);
    }

    /// <summary>
    /// LostFocus commit. Identical to <see cref="CommitRename"/> for real rows, but a
    /// placeholder with a bad/colliding name is discarded rather than kept open — clicking
    /// away must never trap focus in the edit box.
    /// </summary>
    public void CommitRenameFromBlur(FileRowViewModel row)
    {
        if (row.IsNewPlaceholder)
            CommitNewEntry(row, fromBlur: true);
        else
            CommitRename(row);
    }

    /// <summary>
    /// Resolves a new-entry placeholder: empty name discards it; a valid free name creates
    /// the folder/file and reloads; a bad/colliding name stays editing (Enter) or is
    /// discarded (<paramref name="fromBlur"/>).
    /// </summary>
    private void CommitNewEntry(FileRowViewModel row, bool fromBlur)
    {
        var name = row.EditName.Trim();
        if (name.Length == 0)
        {
            DiscardPlaceholder(row);
            return;
        }

        if (NewEntryNameError(name) is { } error)
        {
            if (fromBlur)
                DiscardPlaceholder(row);
            else
                StatusText = error; // stay in edit mode so the user can fix the name
            return;
        }

        try
        {
            var (provider, localParent) = Registry.Resolve(CurrentPath);
            if (row.IsDirectory)
                FileOps.CreateFolder(provider, localParent, name);
            else
                FileOps.CreateFile(provider, localParent, name);
        }
        // NotSupportedException: capability belt — a provider without CanCreateEmptyDir /
        // CanCreateFile degrades to a graceful no-op (P5c disables the command as well).
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            if (fromBlur)
                DiscardPlaceholder(row);
            else
                StatusText = e.Message;
            return;
        }

        _editingPlaceholder = null;
        StartLoad(preserveSelection: false, selectAfter: name, selectFirst: false);
    }

    /// <summary>Null when <paramref name="name"/> is a legal, free entry name; else the reason.</summary>
    private string? NewEntryNameError(string name)
    {
        if (name.Contains('/') || name.Contains('\\'))
            return "Name cannot contain path separators";
        var (provider, localParent) = Registry.Resolve(CurrentPath);
        var localTarget = PathUtil.IsRemote(CurrentPath)
            ? localParent.TrimEnd('/') + "/" + name
            : Path.Combine(localParent, name);
        if (provider.DirectoryExists(localTarget) || provider.FileExists(localTarget))
            return $"\"{name}\" already exists";
        return null;
    }

    /// <summary>Removes the synthetic placeholder row without touching the filesystem.</summary>
    private void DiscardPlaceholder(FileRowViewModel row)
    {
        Rows.Remove(row);
        if (ReferenceEquals(_editingPlaceholder, row))
            _editingPlaceholder = null;
        if (Selection.SelectedItem is null && Rows.Count > 0)
            Selection.Select(0);
        UpdateStatus();
    }

    /// <summary>
    /// Runs the rename off the UI thread so a slow (cross-volume) move never blocks.
    /// A single OS move is not interruptible mid-flight, so there is no true mid-move
    /// cancel — same-volume renames are effectively instant anyway.
    /// </summary>
    private async Task RunRenameAsync(string fullPath, string newName)
    {
        var ok = true;
        try
        {
            // fullPath is the provider-local path (FileEntry.FullPath); resolve the provider
            // from CurrentPath (the pane's full URL) to get the correct provider instance.
            var (provider, _) = Registry.Resolve(CurrentPath);
            await RenameScheduler(() => FileOps.Rename(provider, fullPath, newName));
        }
        // NotSupportedException: capability belt — a provider without CanRename no-ops.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            ok = false;
        }

        if (ok)
            StartLoad(preserveSelection: false, selectAfter: newName, selectFirst: false);
    }

    public void CancelRename(FileRowViewModel row)
    {
        if (row.IsNewPlaceholder)
            DiscardPlaceholder(row);
        else
            row.IsEditing = false;
    }

    [RelayCommand]
    public void NewFolder()
    {
        // Capability gate: no-op when the provider doesn't support directory creation.
        var (provider, _) = Registry.Resolve(CurrentPath);
        if (!provider.Capabilities.CanCreateEmptyDir)
            return;
        BeginNewEntry(isDirectory: true, baseName: "New folder");
    }

    [RelayCommand]
    public void NewFile()
    {
        // Capability gate: no-op when the provider doesn't support file creation.
        var (provider, _) = Registry.Resolve(CurrentPath);
        if (!provider.Capabilities.CanCreateFile)
            return;
        BeginNewEntry(isDirectory: false, baseName: "New file");
    }

    /// <summary>
    /// Inserts an editable placeholder row (no disk write) so the user names the entry in
    /// place; <see cref="CommitNewEntry"/> creates it on commit.
    /// </summary>
    private void BeginNewEntry(bool isDirectory, string baseName)
    {
        var (provider, localParent) = Registry.Resolve(CurrentPath);
        var suggested = FileOps.SuggestEntryName(provider, localParent, baseName);
        var row = FileRowViewModel.NewPlaceholder(CurrentPath, suggested, isDirectory);
        _editingPlaceholder = row;
        var insertAt = Rows.Count > 0 && Rows[0].IsParentNav ? 1 : 0;
        Rows.Insert(insertAt, row);
        Selection.Select(insertAt);
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

        // Remote paths get manual refresh only — FileSystemWatcher cannot watch a URI.
        if (PathUtil.IsRemote(CurrentPath))
            return;

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
