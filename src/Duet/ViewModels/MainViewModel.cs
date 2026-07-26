using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duet.Core.Operations;

namespace Duet.ViewModels;

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
    public string PromptGlyph => IsMacChrome ? " ❯" : " $";

    public MainViewModel(string leftPath, string rightPath, ChromeKind? chrome = null)
    {
        Chrome = chrome ?? Program.Options.Chrome;
        Left = new PaneViewModel(leftPath);
        Right = new PaneViewModel(rightPath);
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
            ActivePane.NavigateTo(parent);
            ActivePane.SelectByName(Path.GetFileName(candidate));
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
