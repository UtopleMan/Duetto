using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Core.Remote;
using Duetto.Core.State;
using Renci.SshNet.Common;

namespace Duetto.ViewModels;

public sealed record Place(string Name, string Path, string Color);

// Carries the stored connection so the click handler can open ConnectWindow when the connection is not live.
public sealed record RemotePlace(string Name, string Id, string InitialRemotePath, StoredConnection Stored);

public partial class MainViewModel : ObservableObject, IDisposable
{
    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    public ChromeKind Chrome { get; }
    public bool IsWinChrome => Chrome == ChromeKind.Win;
    public bool IsMacChrome => Chrome == ChromeKind.Mac;
    public bool IsGnomeChrome => Chrome == ChromeKind.Gnome;
    public IReadOnlyList<Place> Places { get; }

    public System.Collections.ObjectModel.ObservableCollection<RemotePlace> RemotePlaces { get; } = [];

    [ObservableProperty]
    private bool _remotePlacesVisible;
    public static string UserAtHost { get; } = $"{Environment.UserName}@{Environment.MachineName.Split('.')[0]}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTransfer))]
    private IStripOperation? _activeOperation;

    public TransferViewModel? ActiveTransfer => ActiveOperation as TransferViewModel;

    public FileSystemRegistry Registry { get; }

    public ConnectionManager ConnectionManager { get; }

    public ConnectionStore ConnectionStore { get; }

    public SmbConnectionManager SmbConnectionManager { get; }

    public SmbConnectionStore SmbConnectionStore { get; }

    public S3ConnectionManager S3ConnectionManager { get; }

    public S3ConnectionStore S3ConnectionStore { get; }

    public HostKeyStore HostKeyStore { get; }

    public SecretCodec Codec { get; }

    // Seam for tests; production routes through the owning provider's Delete so remote paths get a hook later.
    public Func<string, string?> TrashFn { get; set; }

    public Func<Action<CancellationToken>, CancellationToken, Task> DeleteScheduler { get; set; }
        = static (work, ct) => Task.Run(() => work(ct), ct);

    public Task DeleteCompletion { get; private set; } = Task.CompletedTask;

    // Runs the remote-file download off the UI thread; tests swap in an inline runner so the
    // download and launch complete deterministically. Mirrors DeleteScheduler.
    public Func<Action<CancellationToken>, CancellationToken, Task> OpenScheduler { get; set; }
        = static (work, ct) => Task.Run(() => work(ct), ct);

    public Task OpenCompletion { get; private set; } = Task.CompletedTask;

    private readonly RemoteFileOpener _remoteOpener;

    public CommandBarViewModel CommandBar { get; }
    public SearchViewModel Search { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDirName))]
    private PaneViewModel _activePane;

    public PaneViewModel InactivePane => ActivePane == Left ? Right : Left;
    public string ActiveDirName => ActivePane.DirName;
    public static string SearchHint => OperatingSystem.IsMacOS() ? "⌘F" : "Ctrl F";
    public string PromptGlyph => IsMacChrome ? " ❯" : " $";

    // Test constructor. ConnectionManager, ConnectionStore, HostKeyStore, and Codec are not
    // wired unless passed explicitly; a null registry yields a new local-only one.
    public MainViewModel(
        string leftPath,
        string rightPath,
        ChromeKind? chrome = null,
        FileSystemRegistry? registry = null,
        ConnectionManager? connectionManager = null,
        ConnectionStore? connectionStore = null,
        HostKeyStore? hostKeyStore = null,
        SecretCodec? codec = null,
        SessionStore? sessionStore = null,
        SmbConnectionManager? smbConnectionManager = null,
        SmbConnectionStore? smbConnectionStore = null,
        S3ConnectionManager? s3ConnectionManager = null,
        S3ConnectionStore? s3ConnectionStore = null,
        string? remoteOpenTempRoot = null)
    {
        _sessionStore = sessionStore;
        Registry = registry ?? new FileSystemRegistry();
        HostKeyStore = hostKeyStore ?? new HostKeyStore();
        ConnectionManager = connectionManager ?? new ConnectionManager(Registry, HostKeyStore);
        ConnectionStore = connectionStore ?? new ConnectionStore(":memory:", _ => null, (_, _) => { });
        SmbConnectionManager = smbConnectionManager ?? new SmbConnectionManager(Registry);
        SmbConnectionStore = smbConnectionStore ?? new SmbConnectionStore(":memory:", _ => null, (_, _) => { });
        S3ConnectionManager = s3ConnectionManager ?? new S3ConnectionManager(Registry);
        S3ConnectionStore = s3ConnectionStore ?? new S3ConnectionStore(":memory:", _ => null, (_, _) => { });
        Codec = codec ?? new SecretCodec();

        TrashFn = TrashViaProvider;
        Chrome = chrome ?? Program.Options.Chrome;
        Left = new PaneViewModel(leftPath, Registry);
        Right = new PaneViewModel(rightPath, Registry);
        _remoteOpener = new RemoteFileOpener(Registry, p => Left.LaunchFile(p), remoteOpenTempRoot);
        Left.OpenRemoteFile = row => StartRemoteFileOpen(Left, row);
        Right.OpenRemoteFile = row => StartRemoteFileOpen(Right, row);
        Left.Drives.PaneSide = "left";
        Right.Drives.PaneSide = "right";
        WirePopoverSeams(Left.Drives);
        WirePopoverSeams(Right.Drives);
        _activePane = Left;
        Left.IsActive = true;
        Places = BuildPlaces();
        RebuildRemotePlaces();
        CommandBar = new CommandBarViewModel(() => ActivePane.CurrentPath);
        CommandBar.CommandFinished += () =>
        {
            Left.Reload(preserveSelection: true);
            Right.Reload(preserveSelection: true);
        };
        Search = new SearchViewModel(() => ActivePane.CurrentPath, Registry);
        Search.ConnectionNameResolver = id =>
        {
            foreach (var stored in ConnectionStore.Load())
            {
                if (string.Equals(stored.Id, id, StringComparison.OrdinalIgnoreCase))
                    return stored.Name;
            }
            return null;
        };
        Search.RevealRequested += entry =>
        {
            var fullAddress = ToAddress(Search.ScopeDir, entry.FullPath);
            var dir = PathUtil.Parent(fullAddress);
            if (dir is not null)
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
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivePane))
                Search.RefreshSearchSupported();
        };
        Search.RefreshSearchSupported();
    }

    private readonly SessionStore? _sessionStore;

    public static string StartFolder(string? folder) =>
        folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // A command-line folder wins for the left pane; otherwise each pane restores its saved
    // directory when it still exists locally. Missing or remote (sftp://…) saved paths fall back to home.
    public static (string Left, string Right) ResolveStartupPaths(
        string? folderArg, SessionState? saved, string home)
    {
        static bool Usable(string? p) => p is not null && Directory.Exists(p);
        var left = folderArg ?? (Usable(saved?.LeftPath) ? saved!.LeftPath : home);
        var right = Usable(saved?.RightPath) ? saved!.RightPath : home;
        return (left, right);
    }

    public void SaveSession() =>
        _sessionStore?.Save(new SessionState(Left.CurrentPath, Right.CurrentPath));

    public MainViewModel()
        : this(LoadProductionStartup())
    {
    }

    // Single file read: null store when headless (no session.json IO).
    private static (SessionStore? Store, string Left, string Right) LoadProductionStartup()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var store = Program.Options.Headless ? null : new SessionStore(AppPaths.SessionJsonPath);
        var (left, right) = ResolveStartupPaths(Program.Options.Folder, store?.Load(), home);
        return (store, left, right);
    }

    private MainViewModel((SessionStore? Store, string Left, string Right) startup)
        : this(
            startup.Left,
            startup.Right,
            registry: new FileSystemRegistry(),
            connectionStore: new ConnectionStore(AppPaths.ConnectionsJsonPath),
            hostKeyStore: BuildProductionHostKeyStore(),
            sessionStore: startup.Store,
            smbConnectionStore: new SmbConnectionStore(AppPaths.SmbConnectionsJsonPath),
            s3ConnectionStore: new S3ConnectionStore(AppPaths.S3ConnectionsJsonPath))
    {
        // Production only: run listing and rename off the UI thread. Tests use the
        // explicit ctor above and keep the default inline schedulers for deterministic asserts.
        Left.LoadScheduler = PaneViewModel.BackgroundScheduler;
        Right.LoadScheduler = PaneViewModel.BackgroundScheduler;
        Left.RenameScheduler = PaneViewModel.BackgroundRenameScheduler;
        Right.RenameScheduler = PaneViewModel.BackgroundRenameScheduler;
    }

    // Seam: replace in tests to capture dialog-open calls without instantiating a real window.
    public Action<StoredConnection?, PaneViewModel> OpenConnectDialog { get; set; } = (_, _) => { };

    // Seam: replace in tests to execute the action synchronously without Task.Run.
    public Func<Action, Task> ConnectScheduler { get; set; } =
        static work => Task.Run(work);

    // Background connect swallows the documented connection exceptions (opening the dialog
    // prefilled instead); all other exceptions propagate as unobserved task faults — genuine bugs.
    public void ConnectToShare(StoredConnection stored, PaneViewModel pane)
    {
        if (ConnectionManager.IsConnected(stored.Id))
        {
            var path = $"sftp://{stored.Id}{stored.InitialRemotePath}";
            pane.NavigateTo(path);
            return;
        }

        var secret = ConnectionStore.ResolveSecret(stored, Codec);
        if (secret is not null)
        {
            var info = ConnectionStore.ResolveInfo(stored);
            var capturedPath = $"sftp://{info.Id}{info.InitialRemotePath}";
            // Capture the seam NOW: a second share click may overwrite OpenConnectDialog
            // (each call site wires its own owner window) before this background connect
            // fails — the failure must open the dialog wired for THIS click, not a later one.
            var openDialog = OpenConnectDialog;
            _ = ConnectScheduler(() =>
            {
                try
                {
                    ConnectionManager.Connect(info, secret);
                }
                // SshConnectionException ⊂ SshException; listed explicitly for documentation
                catch (Exception ex) when (ex is SshAuthenticationException
                    or SshConnectionException
                    or SocketException
                    or HostKeyChangedException
                    or ObjectDisposedException
                    or SshException
                    or IOException
                    or InvalidOperationException)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => openDialog(stored, pane));
                    return;
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() => pane.NavigateTo(capturedPath));
            });
            return;
        }

        OpenConnectDialog(stored, pane);
    }

    // Seam: replace in tests to capture SMB dialog-open calls without instantiating a window.
    public Action<StoredSmbConnection?, PaneViewModel> OpenSmbConnectDialog { get; set; } = (_, _) => { };

    // SMB counterpart of ConnectToShare. Connection/auth failures reopen the dialog prefilled;
    // any other exception surfaces as an unobserved task fault (a genuine bug).
    public void ConnectToSmbShare(StoredSmbConnection stored, PaneViewModel pane)
    {
        if (SmbConnectionManager.IsConnected(stored.Id))
        {
            pane.NavigateTo($"smb://{stored.Id}{stored.InitialPath}");
            return;
        }

        var secret = SmbConnectionStore.ResolveSecret(stored, Codec);
        if (secret is not null)
        {
            var info = SmbConnectionStore.ResolveInfo(stored);
            var capturedPath = $"smb://{info.Id}{info.InitialPath}";
            var openDialog = OpenSmbConnectDialog;
            _ = ConnectScheduler(() =>
            {
                try
                {
                    SmbConnectionManager.Connect(info, secret);
                }
                // SmbConnectionException / SmbAuthenticationException ⊂ IOException; listed for documentation.
                catch (Exception ex) when (ex is SmbAuthenticationException
                    or SmbConnectionException
                    or SocketException
                    or ObjectDisposedException
                    or IOException
                    or InvalidOperationException)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => openDialog(stored, pane));
                    return;
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() => pane.NavigateTo(capturedPath));
            });
            return;
        }

        OpenSmbConnectDialog(stored, pane);
    }

    // Seam: replace in tests to capture S3 dialog-open calls without instantiating a window.
    public Action<StoredS3Connection?, PaneViewModel> OpenS3ConnectDialog { get; set; } = (_, _) => { };

    // S3 counterpart of ConnectToShare. Connection/auth failures reopen the dialog prefilled;
    // any other exception surfaces as an unobserved task fault (a genuine bug).
    public void ConnectToS3Share(StoredS3Connection stored, PaneViewModel pane)
    {
        if (S3ConnectionManager.IsConnected(stored.Id))
        {
            pane.NavigateTo($"s3://{stored.Id}{stored.InitialPath}");
            return;
        }

        var secret = S3ConnectionStore.ResolveSecret(stored, Codec);
        if (secret is not null)
        {
            var info = S3ConnectionStore.ResolveInfo(stored);
            var capturedPath = $"s3://{info.Id}{info.InitialPath}";
            var openDialog = OpenS3ConnectDialog;
            _ = ConnectScheduler(() =>
            {
                try
                {
                    S3ConnectionManager.Connect(info, secret);
                }
                // S3ConnectionException / S3AuthenticationException are surfaced from the adapter;
                // SocketException/IOException cover transport faults. Listed for documentation.
                catch (Exception ex) when (ex is S3AuthenticationException
                    or S3ConnectionException
                    or SocketException
                    or ObjectDisposedException
                    or IOException
                    or InvalidOperationException)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => openDialog(stored, pane));
                    return;
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() => pane.NavigateTo(capturedPath));
            });
            return;
        }

        OpenS3ConnectDialog(stored, pane);
    }

    // Call after Left/Right panes are constructed.
    private void WirePopoverSeams(DrivePopoverViewModel drives)
    {
        drives.ListConnections = () => ConnectionStore.Load();
        drives.IsConnected = id => ConnectionManager.IsConnected(id);
        drives.ListSmbConnections = () => SmbConnectionStore.Load();
        drives.IsSmbConnected = id => SmbConnectionManager.IsConnected(id);
        drives.ListS3Connections = () => S3ConnectionStore.Load();
        drives.IsS3Connected = id => S3ConnectionManager.IsConnected(id);
    }

    public void RebuildRemotePlaces()
    {
        RemotePlaces.Clear();
        foreach (var stored in ConnectionStore.Load())
            RemotePlaces.Add(new RemotePlace(stored.Name, stored.Id, stored.InitialRemotePath, stored));
        RemotePlacesVisible = RemotePlaces.Count > 0;
    }

    private static HostKeyStore BuildProductionHostKeyStore()
    {
        var persistence = JsonHostKeyPersistence.Attach(AppPaths.HostKeysJsonPath);
        return new HostKeyStore(persistence);
    }

    [RelayCommand]
    public void SwitchPane() => Activate(InactivePane);

    [RelayCommand]
    public void NavigatePlace(Place place) => ActivePane.NavigateTo(place.Path);

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
            StartTransfer(entries.Select(e => e.FullPath).ToList(), Left.CurrentPath, mode,
                sourcePane: null, sourceScope: Search.ScopeDir);
            return;
        }

        var source = ActivePane;
        var paths = source.SelectedRows.Select(r => r.Entry.FullPath).ToList();
        StartTransfer(paths, InactivePane.CurrentPath, mode, source, sourceScope: source.CurrentPath);
    }

    // paths are provider-local (what rows/hits carry); destinationDir is a full address.
    // sourceScope is the full address the sources live under and resolves the source provider —
    // the paths themselves are already provider-local.
    private void StartTransfer(
        IReadOnlyList<string> paths, string destinationDir, TransferMode mode,
        PaneViewModel? sourcePane, string sourceScope)
    {
        if (paths.Count == 0 || ActiveOperation is { IsFinished: false })
            return;

        var (srcProvider, _) = Registry.Resolve(sourceScope);
        var (destProvider, destLocalDir) = Registry.Resolve(destinationDir);

        ActiveOperation?.Dispose();
        var session = TransferEngine.Start(paths, srcProvider, destLocalDir, destProvider, mode,
            displayDestination: destinationDir);
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
        // Capability gate: no-op when the owning provider doesn't support delete.
        var checkPath = Search.IsActive ? Search.ScopeDir : ActivePane.CurrentPath;
        var (gateProvider, _) = Registry.Resolve(checkPath);
        if (!gateProvider.Capabilities.CanDelete)
            return;

        // Rows and search hits carry provider-local paths ("/home/user/f.txt" on a remote
        // pane/scope); rebuild the full scheme://id/... address so TrashFn's Registry.Resolve
        // hits the owning provider and can never touch a same-named local path.
        var fromSearch = Search.IsActive;
        // Delete acts only on explicitly marked rows — never the file merely under the cursor.
        var paths = (fromSearch
                ? Search.SelectedEntries.Select(e => ToAddress(Search.ScopeDir, e.FullPath))
                : ActivePane.MarkedRows.Select(r => ToAddress(ActivePane.CurrentPath, r.Entry.FullPath)))
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

        // Capture the trash capability NOW (from the first path's provider) — the active pane
        // may change while the async delete runs, so it cannot be resolved at finish time.
        var hasTrash = Registry.Resolve(paths[0]).Provider.Capabilities.HasTrash;
        DeleteCompletion = RunDeleteAsync(paths, op, cts.Token, fromSearch, hasTrash);
    }

    private static string ToAddress(string panePath, string rowPath) =>
        PathUtil.ToAddress(panePath, rowPath);

    // Routes the delete through the owning provider — local paths go to the OS trash; a remote
    // provider without HasTrash deletes permanently on its side.
    private string? TrashViaProvider(string path)
    {
        var (provider, localPath) = Registry.Resolve(path);
        provider.Delete(localPath, toTrash: true);
        return null;
    }

    // Checks cancellation before every item (cancel stops before the next one; already-trashed
    // items stay trashed). A per-item failure is swallowed so one bad entry doesn't abort the batch.
    private async Task RunDeleteAsync(
        IReadOnlyList<string> paths, SimpleOperationViewModel op, CancellationToken token, bool fromSearch,
        bool hasTrash)
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
                    // NotSupportedException: capability belt — a provider without CanDelete
                    // skips the item instead of faulting the batch.
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException
                        or FileNotFoundException or NotSupportedException)
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
            // trashed holds full addresses; rebase each row the same way before comparing.
            foreach (var row in Search.Results
                         .Where(r => trashed.Contains(ToAddress(Search.ScopeDir, r.Entry.FullPath))).ToList())
                Search.Results.Remove(row);
        }

        Left.Reload(preserveSelection: true);
        Right.Reload(preserveSelection: true);

        if (token.IsCancellationRequested)
        {
            op.Dismiss();
        }
        else
        {
            // "Moved to Trash" vs "Deleted" keyed on the owning provider's HasTrash,
            // captured at DeleteSelected time (remote deletes are permanent).
            var n = trashed.Count;
            var what = n == 1 ? "item" : "items";
            var finalTitle = hasTrash
                ? $"Moved {n} {what} to Trash"
                : $"Deleted {n} {what}";
            op.Finish(finalTitle);
        }
    }

    // Enter / double-click on a remote file row: download to temp behind a progress strip,
    // then launch the local copy. Same single-slot guard as copy/delete.
    private void StartRemoteFileOpen(PaneViewModel pane, FileRowViewModel row)
    {
        if (ActiveOperation is { IsFinished: false })
            return;

        var address = ToAddress(pane.CurrentPath, row.Entry.FullPath);
        var cts = new CancellationTokenSource();
        var op = new SimpleOperationViewModel($"Opening {row.Name}…", cts);
        op.Dismissed += () =>
        {
            if (ReferenceEquals(ActiveOperation, op))
                ActiveOperation = null;
            op.Dispose();
        };
        ActiveOperation = op;

        OpenCompletion = RunOpenAsync(address, row.Name, op, cts.Token);
    }

    // The await resumes on the captured UI context (like RunDeleteAsync), so Launch/Finish run
    // on the UI thread. A failed download dismisses the strip quietly — the app never crashes.
    private async Task RunOpenAsync(
        string address, string name, SimpleOperationViewModel op, CancellationToken token)
    {
        string? tempPath = null;
        var failed = false;
        try
        {
            await OpenScheduler(ct => tempPath = _remoteOpener.Download(address, ct), token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or SshException
            or SocketException
            or HostKeyChangedException)
        {
            failed = true;
        }

        if (failed || token.IsCancellationRequested || tempPath is null)
        {
            op.Dismiss();
            return;
        }

        _remoteOpener.Launch(tempPath);
        op.Finish($"Opened {name}");
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
        ConnectionManager.Dispose();
        SmbConnectionManager.Dispose();
        S3ConnectionManager.Dispose();
        _remoteOpener.Dispose();
    }
}
