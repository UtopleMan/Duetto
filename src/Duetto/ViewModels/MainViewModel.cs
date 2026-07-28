using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;

namespace Duetto.ViewModels;

public sealed record Place(string Name, string Path, string Color);

public partial class MainViewModel : ObservableObject, IDisposable
{
    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    public ChromeKind Chrome { get; }
    public bool IsWinChrome => Chrome == ChromeKind.Win;
    public bool IsMacChrome => Chrome == ChromeKind.Mac;
    public bool IsGnomeChrome => Chrome == ChromeKind.Gnome;
    public IReadOnlyList<Place> Places { get; }
    public static string UserAtHost { get; } = $"{Environment.UserName}@{Environment.MachineName.Split('.')[0]}";

    /// <summary>The single strip slot: a transfer, a delete, a rename, or a slow listing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTransfer))]
    private IStripOperation? _activeOperation;

    /// <summary>Convenience view of the slot when it holds a transfer (used by tests + transfer wiring).</summary>
    public TransferViewModel? ActiveTransfer => ActiveOperation as TransferViewModel;

    /// <summary>Maps a path to the provider that owns it (local disk, later SFTP/S3).</summary>
    public FileSystemRegistry Registry { get; } = new();

    /// <summary>
    /// Moves a path to the OS trash. Seam for tests; production routes through the owning
    /// provider's <see cref="IFileSystemProvider.Delete"/> so remote paths get a hook later.
    /// </summary>
    public Func<string, string?> TrashFn { get; set; }

    /// <summary>Schedules the delete loop. Default runs it on a worker thread; tests inject inline.</summary>
    public Func<Action<CancellationToken>, CancellationToken, Task> DeleteScheduler { get; set; }
        = static (work, ct) => Task.Run(() => work(ct), ct);

    /// <summary>Completes when the current delete finishes; tests await this to settle.</summary>
    public Task DeleteCompletion { get; private set; } = Task.CompletedTask;

    public CommandBarViewModel CommandBar { get; }
    public SearchViewModel Search { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDirName))]
    private PaneViewModel _activePane;

    public PaneViewModel InactivePane => ActivePane == Left ? Right : Left;
    public string ActiveDirName => ActivePane.DirName;
    public static string SearchHint => OperatingSystem.IsMacOS() ? "⌘F" : "Ctrl F";
    public string PromptGlyph => IsMacChrome ? " ❯" : " $";

    public MainViewModel(string leftPath, string rightPath, ChromeKind? chrome = null)
    {
        TrashFn = TrashViaProvider;
        Chrome = chrome ?? Program.Options.Chrome;
        Left = new PaneViewModel(leftPath);
        Right = new PaneViewModel(rightPath);
        Left.Drives.PaneSide = "left";
        Right.Drives.PaneSide = "right";
        _activePane = Left;
        Left.IsActive = true;
        Places = BuildPlaces();
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
                Left.NavigateTo(dir, entry.Name);
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
        // Production only: run listing and rename off the UI thread. Tests use the
        // explicit ctor above and keep the default inline schedulers for deterministic asserts.
        Left.LoadScheduler = PaneViewModel.BackgroundScheduler;
        Right.LoadScheduler = PaneViewModel.BackgroundScheduler;
        Left.RenameScheduler = PaneViewModel.BackgroundRenameScheduler;
        Right.RenameScheduler = PaneViewModel.BackgroundRenameScheduler;
    }

    [RelayCommand]
    public void SwitchPane() => Activate(InactivePane);

    [RelayCommand]
    public void NavigatePlace(Place place) => ActivePane.NavigateTo(place.Path);

    /// <summary>
    /// Address-bar navigation from the search field: resolves ~, relative, and
    /// absolute paths against the active pane. Directories open; files are
    /// revealed (parent opened, file selected). Returns false if nothing exists.
    /// </summary>
    public bool TryNavigatePath(string input)
    {
        var text = input.Trim();
        if (text.Length == 0)
            return false;

        if (text == "~")
            text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        else if (text.StartsWith("~/") || text.StartsWith(@"~\"))
            text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text[2..]);

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(ActivePane.CurrentPath, text));
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        if (Directory.Exists(candidate))
        {
            ActivePane.NavigateTo(candidate);
            Search.Clear();
            return true;
        }

        if (File.Exists(candidate) && Path.GetDirectoryName(candidate) is { } parent)
        {
            ActivePane.NavigateTo(parent, Path.GetFileName(candidate));
            Search.Clear();
            return true;
        }

        return false;
    }

    private static List<Place> BuildPlaces()
    {
        const string folder = "#c8992f";
        const string volume = "#2f6fd0";
        const string muted = "#b6b3a8";
        var places = new List<Place>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Add(string name, string path, string color)
        {
            if (Directory.Exists(path))
                places.Add(new Place(name, path, color));
        }

        Add("Home", home, folder);
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), folder);
        Add("Downloads", Path.Combine(home, "Downloads"), folder);
        Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), folder);
        if (OperatingSystem.IsMacOS())
            Add("Trash", Path.Combine(home, ".Trash"), muted);
        else if (OperatingSystem.IsLinux())
            Add("Trash", Path.Combine(home, ".local/share/Trash/files"), muted);

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                foreach (var vol in Directory.EnumerateDirectories("/Volumes"))
                    Add(Path.GetFileName(vol), vol, volume);
            }
            else
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d =>
                             d is { IsReady: true, DriveType: DriveType.Fixed or DriveType.Removable or DriveType.Network }))
                    Add(drive.Name, drive.RootDirectory.FullName, volume);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return places;
    }

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
        if (paths.Count == 0 || ActiveOperation is { IsFinished: false })
            return;

        ActiveOperation?.Dispose();
        var session = TransferEngine.Start(paths, destinationDir, mode);
        var transfer = new TransferViewModel(session, sourcePane);
        transfer.Dismissed += () =>
        {
            if (ReferenceEquals(ActiveOperation, transfer))
                ActiveOperation = null;
            transfer.Dispose();
            Left.Reload(preserveSelection: true);
            Right.Reload(preserveSelection: true);
        };
        ActiveOperation = transfer;
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        var fromSearch = Search.IsActive;
        var paths = (fromSearch
                ? Search.SelectedEntries.Select(e => e.FullPath)
                : ActivePane.SelectedRows.Select(r => r.Entry.FullPath))
            .ToList();

        if (paths.Count == 0 || ActiveOperation is { IsFinished: false })
            return;

        var cts = new CancellationTokenSource();
        var op = new SimpleOperationViewModel(
            $"Deleting {paths.Count} {(paths.Count == 1 ? "item" : "items")}", cts);
        op.Dismissed += () =>
        {
            if (ReferenceEquals(ActiveOperation, op))
                ActiveOperation = null;
            op.Dispose();
        };
        ActiveOperation = op;

        DeleteCompletion = RunDeleteAsync(paths, op, cts.Token, fromSearch);
    }

    /// <summary>
    /// Default <see cref="TrashFn"/>: routes the delete through the owning provider's trash
    /// (local disk today; a remote provider without <see cref="FileSystemCapabilities.HasTrash"/>
    /// falls back to a permanent delete on its side later). Returns null — the caller ignores it.
    /// </summary>
    private string? TrashViaProvider(string path)
    {
        var (provider, localPath) = Registry.Resolve(path);
        provider.Delete(localPath, toTrash: true);
        return null;
    }

    /// <summary>
    /// Trashes each path on a worker thread, checking cancellation before every item
    /// (cancel stops before the next one; already-trashed items stay trashed). A
    /// per-item failure is swallowed so one bad entry doesn't abort the batch.
    /// </summary>
    private async Task RunDeleteAsync(
        IReadOnlyList<string> paths, SimpleOperationViewModel op, CancellationToken token, bool fromSearch)
    {
        var trashed = new HashSet<string>();
        try
        {
            await DeleteScheduler(ct =>
            {
                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        TrashFn(path);
                        trashed.Add(path);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
                    {
                    }
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
        }

        if (fromSearch)
        {
            foreach (var row in Search.Results.Where(r => trashed.Contains(r.Entry.FullPath)).ToList())
                Search.Results.Remove(row);
        }

        Left.Reload(preserveSelection: true);
        Right.Reload(preserveSelection: true);

        if (token.IsCancellationRequested)
            op.Dismiss();
        else
            op.Finish();
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
        // Cancels an in-flight delete/transfer (SimpleOperationViewModel/TransferSession
        // cancel their token on Dispose) before tearing down the panes.
        ActiveOperation?.Dispose();
        Left.Dispose();
        Right.Dispose();
    }
}
