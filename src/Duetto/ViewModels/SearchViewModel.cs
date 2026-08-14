using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Core.Search;

namespace Duetto.ViewModels;

public enum SizeFilter
{
    Any,
    Over1MB,
    Over10MB,
    Over100MB,
}

public enum DateFilter
{
    Any,
    Today,
    ThisWeek,
    ThisMonth,
}

public partial class SearchResultRowViewModel(SearchHit hit) : ObservableObject
{
    public FileEntry Entry { get; } = hit.Entry;
    public string Name => Entry.Name;
    public string Folder { get; } = hit.RelativeFolder.Length == 0 ? "." : hit.RelativeFolder;
    public string SizeText => FormatUtil.HumanSize(Entry.SizeBytes, Entry.IsDirectory);
    public string ModifiedText => FormatUtil.DateShort(Entry.ModifiedUtc);
    public string MarkColor => Entry.IsDirectory
        ? PaletteLookup.Hex("FolderMark", "#c8992f")
        : PaletteLookup.Hex("FileMark", "#b6b3a8");
}

public partial class SearchViewModel : ObservableObject
{
    private readonly Func<string> _scopeProvider;
    private readonly FileSystemRegistry? _registry;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _debounce;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentsChipLabel))]
    private bool _includeContents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeFilterLabel))]
    private SizeFilter _sizeFilter = SizeFilter.Any;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateFilterLabel))]
    private DateFilter _dateFilter = DateFilter.Any;

    [ObservableProperty]
    private string _scopeDir = "";

    [ObservableProperty]
    private string _matchText = "";

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private string _headerQuery = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchWatermark))]
    private bool _isSearchSupported = true;

    public string SearchWatermark =>
        IsSearchSupported
            ? "Search everything below this folder…"
            : "Search unavailable for this provider";

    [ObservableProperty]
    private bool _isPinned;

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];
    public SelectionModel<SearchResultRowViewModel> Selection { get; } = new() { SingleSelect = false };

    public event Action<FileEntry>? RevealRequested;

    public string ScopeDirName
    {
        get
        {
            var leaf = PathUtil.Leaf(ScopeDir);
            if (leaf.Length > 0)
                return leaf;

            if (PathUtil.ParseRemote(ScopeDir) is { } remote)
            {
                return ConnectionNameResolver(remote.Id) ?? remote.Id;
            }

            return ScopeDir;
        }
    }
    public string ContentsChipLabel => IncludeContents ? "− Contents" : "+ Contents";

    public string SizeFilterLabel => SizeFilter switch
    {
        SizeFilter.Over1MB => "> 1 MB",
        SizeFilter.Over10MB => "> 10 MB",
        SizeFilter.Over100MB => "> 100 MB",
        _ => "Any size",
    };

    public string DateFilterLabel => DateFilter switch
    {
        DateFilter.Today => "Today",
        DateFilter.ThisWeek => "This week",
        DateFilter.ThisMonth => "This month",
        _ => "Any date",
    };

    public Func<string, string?> ConnectionNameResolver { get; set; } = _ => null;

    public SearchViewModel(Func<string> scopeProvider, FileSystemRegistry? registry = null)
    {
        _scopeProvider = scopeProvider;
        _registry = registry;
        Selection.Source = Results;
    }

    public void RefreshSearchSupported()
    {
        var scope = _scopeProvider();
        IsSearchSupported = _registry is null
            || _registry.Resolve(scope).Provider.Capabilities.SupportsSearch;
    }

    partial void OnQueryChanged(string value)
    {
        DebugLog.Write($"search: query changed to '{value}'");
        ScheduleSearch();
    }
    partial void OnIncludeContentsChanged(bool value) => ScheduleSearch();
    partial void OnSizeFilterChanged(SizeFilter value) => ScheduleSearch();
    partial void OnDateFilterChanged(DateFilter value) => ScheduleSearch();

    [RelayCommand]
    public void ToggleContents() => IncludeContents = !IncludeContents;

    public void SetSizeFilter(SizeFilter f) => SizeFilter = f;
    public void SetDateFilter(DateFilter f) => DateFilter = f;

    private void ScheduleSearch()
    {
        if (_debounce is null)
        {
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += (_, _) =>
            {
                _debounce!.Stop();
                _ = StartSearchAsync();
            };
        }

        _debounce.Stop();
        _debounce.Start();
    }

    [RelayCommand]
    public void PinResults()
    {
        if (Results.Count == 0)
            return;
        _cts?.Cancel();
        IsPinned = true;
        Query = "";
    }

    public static bool IsPathLike(string text) =>
        text.StartsWith('~') || Path.IsPathRooted(text) ||
        text.Contains(Path.DirectorySeparatorChar) || text.Contains(Path.AltDirectorySeparatorChar);

    public async Task StartSearchAsync()
    {
        var query = Query.Trim();
        if (query.Length == 0 || IsPathLike(query))
        {
            if (!IsPinned)
            {
                _cts?.Cancel();
                Results.Clear();
                HeaderQuery = "";
                IsActive = false;
                MatchText = "";
                ElapsedText = "";
            }

            return;
        }

        _cts?.Cancel();
        IsPinned = false;
        Results.Clear();
        HeaderQuery = query;
        IsActive = true;
        IsSearching = true;
        ScopeDir = _scopeProvider();
        OnPropertyChanged(nameof(ScopeDirName));
        RefreshSearchSupported();
        var cts = _cts = new CancellationTokenSource();
        var stats = new SearchStats();
        var clock = Stopwatch.StartNew();
        MatchText = "0 matches";

        DebugLog.Write($"search: start '{query}' below {ScopeDir} contents={IncludeContents}");
        try
        {
            var searchEnum = _registry is not null
                ? SearchService.Search(ScopeDir, query, IncludeContents, stats, _registry, cts.Token)
                : SearchService.Search(ScopeDir, query, IncludeContents, stats, cts.Token);
            await foreach (var hit in searchEnum)
            {
                if (!PassesFilters(hit.Entry))
                    continue;
                Results.Add(new SearchResultRowViewModel(hit));
                MatchText = $"{Results.Count} {(Results.Count == 1 ? "match" : "matches")} in {stats.FilesScanned:n0} files";
            }

            MatchText = $"{Results.Count} {(Results.Count == 1 ? "match" : "matches")} in {stats.FilesScanned:n0} files";
            ElapsedText = $"{clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s";
            DebugLog.Write($"search: done '{query}' {Results.Count} hits, {stats.FilesScanned} files scanned");
        }
        catch (OperationCanceledException)
        {
            DebugLog.Write($"search: cancelled '{query}'");
        }
        catch (HostKeyChangedException ex)
        {
            MatchText = $"Host key changed: {ex.Host} — reconnect via the Connect dialog";
            DebugLog.Write($"search: host key changed '{query}': {ex}");
        }
        catch (Exception e)
        {
            DebugLog.Write($"search: FAILED '{query}': {e}");
        }
        finally
        {
            if (_cts == cts)
                IsSearching = false;
        }
    }

    private bool PassesFilters(FileEntry entry)
    {
        var minSize = SizeFilter switch
        {
            SizeFilter.Over1MB => 1L << 20,
            SizeFilter.Over10MB => 10L << 20,
            SizeFilter.Over100MB => 100L << 20,
            _ => 0,
        };
        if (minSize > 0 && (entry.IsDirectory || entry.SizeBytes < minSize))
            return false;

        if (DateFilter != DateFilter.Any)
        {
            var cutoff = DateFilter switch
            {
                DateFilter.Today => DateTime.UtcNow.Date,
                DateFilter.ThisWeek => DateTime.UtcNow.AddDays(-7),
                _ => DateTime.UtcNow.AddMonths(-1),
            };
            if (entry.ModifiedUtc < cutoff)
                return false;
        }

        return true;
    }

    public void RevealSelected()
    {
        if (Selection.SelectedItem is { } row)
            RevealRequested?.Invoke(row.Entry);
    }

    public IReadOnlyList<FileEntry> SelectedEntries =>
        Selection.SelectedItems.OfType<SearchResultRowViewModel>().Select(r => r.Entry).ToList();

    [RelayCommand]
    public void Clear()
    {
        _cts?.Cancel();
        _debounce?.Stop();
        IsPinned = false;
        Query = "";
        Results.Clear();
        IsActive = false;
        IsSearching = false;
        MatchText = "";
        ElapsedText = "";
    }
}
