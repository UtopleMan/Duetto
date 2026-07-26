using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duet.Core.FileSystem;
using Duet.Core.Search;

namespace Duet.ViewModels;

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
    public string MarkColor => Entry.IsDirectory ? "#c8992f" : "#b6b3a8";
}

public partial class SearchViewModel : ObservableObject
{
    private readonly Func<string> _scopeProvider;
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

    /// <summary>"Open as pane": results stay visible after the query clears, until Esc or a new search.</summary>
    [ObservableProperty]
    private bool _isPinned;

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];
    public SelectionModel<SearchResultRowViewModel> Selection { get; } = new() { SingleSelect = false };

    /// <summary>Raised when a result should be revealed in the left pane.</summary>
    public event Action<FileEntry>? RevealRequested;

    public string ScopeDirName => Path.GetFileName(ScopeDir.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : ScopeDir;
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

    public SearchViewModel(Func<string> scopeProvider)
    {
        _scopeProvider = scopeProvider;
        Selection.Source = Results;
    }

    partial void OnQueryChanged(string value) => ScheduleSearch();
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

    /// <summary>
    /// Path-like input ("/…", "~…", "C:\…", or anything with a separator) is an
    /// address, not a query — Enter navigates instead, and no search runs.
    /// </summary>
    public static bool IsPathLike(string text) =>
        text.StartsWith('~') || Path.IsPathRooted(text) ||
        text.Contains(Path.DirectorySeparatorChar) || text.Contains(Path.AltDirectorySeparatorChar);

    /// <summary>Runs the search immediately (debounce bypassed; used by the timer and tests).</summary>
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
        var cts = _cts = new CancellationTokenSource();
        var stats = new SearchStats();
        var clock = Stopwatch.StartNew();
        MatchText = "0 matches";

        try
        {
            await foreach (var hit in SearchService.Search(ScopeDir, query, IncludeContents, stats, cts.Token))
            {
                if (!PassesFilters(hit.Entry))
                    continue;
                Results.Add(new SearchResultRowViewModel(hit));
                MatchText = $"{Results.Count} {(Results.Count == 1 ? "match" : "matches")} in {stats.FilesScanned:n0} files";
            }

            MatchText = $"{Results.Count} {(Results.Count == 1 ? "match" : "matches")} in {stats.FilesScanned:n0} files";
            ElapsedText = $"{clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s";
        }
        catch (OperationCanceledException)
        {
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
