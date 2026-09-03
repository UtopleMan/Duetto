using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Xml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Preview;
using Duetto.Core.Remote;
using Renci.SshNet.Common;

namespace Duetto.ViewModels;

public partial class ViewerViewModel : ObservableObject
{
    private sealed record PdfDocumentPreview(PdfPageRenderer Renderer, PdfPage FirstPage);

    private readonly PreviewLoader _loader;
    private readonly PreviewLimits _limits;
    private readonly List<int> _matches = [];
    private CancellationTokenSource? _cts;
    private PdfPageRenderer? _pdfRenderer;

    public ViewerViewModel(FileSystemRegistry registry, PreviewLimits? limits = null)
    {
        _loader = new PreviewLoader(registry);
        _limits = limits ?? PreviewLimits.Default;
    }

    public Func<Action<CancellationToken>, CancellationToken, Task> LoadScheduler { get; set; }
        = static (work, ct) => Task.Run(() => work(ct), ct);

    public Task LoadCompletion { get; private set; } = Task.CompletedTask;

    public event Action<string>? OpenInDefaultAppRequested;

    public event Action<int>? ScrollToLineRequested;

    public ObservableCollection<PreviewLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _fileName = "";

    [ObservableProperty]
    private string _addressText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _encodingLabel = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _sizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTruncation))]
    private string _truncationText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMode), nameof(IsImageMode), nameof(IsVectorMode),
        nameof(IsPdfMode), nameof(IsEmptyFile), nameof(IsFindVisible), nameof(FooterHintText))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsTextMode), nameof(IsImageMode),
        nameof(IsVectorMode), nameof(IsPdfMode), nameof(IsEmptyFile), nameof(IsFindVisible),
        nameof(FooterHintText))]
    private string _errorText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText), nameof(IsTextMode), nameof(IsImageMode),
        nameof(IsVectorMode), nameof(IsPdfMode), nameof(IsEmptyFile), nameof(ShowLineNumbers),
        nameof(IsFindVisible), nameof(FooterHintText))]
    private PreviewKind _kind;

    [ObservableProperty]
    private bool _isWrapped;

    [ObservableProperty]
    private IImage? _image;

    [ObservableProperty]
    private string _imageDimensionsText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _pageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _pageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFindVisible))]
    private bool _isFindOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchPositionText))]
    private int _matchCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchPositionText))]
    private int _currentMatchIndex = -1;

    [ObservableProperty]
    private string _findQuery = "";

    public string HeaderText => SizeText.Length == 0
        ? FileName
        : $"{FileName}  ·  {SizeText}  ·  {DetailLabel}";

    public bool HasError => ErrorText.Length > 0;

    public bool HasTruncation => TruncationText.Length > 0;

    public bool IsTextMode => IsContentVisible && Kind is PreviewKind.Text or PreviewKind.Hex;

    public bool IsImageMode => IsContentVisible && Kind == PreviewKind.Image;

    public bool IsVectorMode => IsContentVisible && Kind == PreviewKind.Vector;

    public bool IsPdfMode => IsContentVisible && Kind == PreviewKind.Pdf;

    public bool IsEmptyFile => IsContentVisible && Kind == PreviewKind.Empty;

    public string PageText => PageCount == 0 ? "" : $"{PageIndex + 1} / {PageCount}";

    public string FooterHintText => IsPdfMode
        ? "Esc close · PgUp / PgDn page"
        : "Esc close · W wrap · n / N next / previous match";

    public bool ShowLineNumbers => Kind == PreviewKind.Text;

    public bool IsFindVisible => IsFindOpen && IsTextMode;

    private bool IsContentVisible => !IsLoading && !HasError;

    private string DetailLabel => EncodingLabel.Length > 0 ? EncodingLabel : KindLabel;

    private string KindLabel => Kind switch
    {
        PreviewKind.Image => "Image",
        PreviewKind.Vector => "SVG",
        PreviewKind.Pdf => "PDF",
        PreviewKind.Hex => "Binary",
        PreviewKind.Empty => "Empty",
        _ => "Text",
    };

    public void Show(string fullAddress, string displayName)
    {
        CancelLoad();
        ClosePdf();

        var cts = new CancellationTokenSource();
        _cts = cts;

        FileName = displayName;
        AddressText = fullAddress;
        ErrorText = "";
        TruncationText = "";
        EncodingLabel = "";
        SizeText = "";
        ImageDimensionsText = "";
        Image = null;
        Kind = PreviewKind.Empty;
        Lines.Clear();
        ClearFind();
        IsLoading = true;

        LoadCompletion = RunLoadAsync(fullAddress, cts);
    }

    public void Cancel()
    {
        CancelLoad();
        ClosePdf();
        IsLoading = false;
    }

    public string MatchPositionText => MatchCount == 0
        ? FindQuery.Length == 0 ? "" : "no matches"
        : $"{CurrentMatchIndex + 1} of {MatchCount}";

    [RelayCommand]
    public void ToggleWrap() => IsWrapped = !IsWrapped;

    [RelayCommand]
    public void NextPage() => ShowPage(PageIndex + 1);

    [RelayCommand]
    public void PreviousPage() => ShowPage(PageIndex - 1);

    public void OpenFind()
    {
        if (IsTextMode)
            IsFindOpen = true;
    }

    [RelayCommand]
    public void CloseFind()
    {
        IsFindOpen = false;
        FindQuery = "";
    }

    [RelayCommand]
    public void FindNext() => StepMatch(1);

    [RelayCommand]
    public void FindPrevious() => StepMatch(-1);

    partial void OnFindQueryChanged(string value) => Rematch(value);

    private void StepMatch(int direction)
    {
        if (_matches.Count == 0)
            return;

        CurrentMatchIndex = (CurrentMatchIndex + direction + _matches.Count) % _matches.Count;
        ScrollToLineRequested?.Invoke(_matches[CurrentMatchIndex]);
    }

    private void Rematch(string query)
    {
        _matches.Clear();
        foreach (var line in Lines)
            line.IsMatch = false;

        if (query.Length > 0)
        {
            for (var index = 0; index < Lines.Count; index++)
            {
                if (!Lines[index].Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;

                Lines[index].IsMatch = true;
                _matches.Add(index);
            }
        }

        MatchCount = _matches.Count;
        CurrentMatchIndex = _matches.Count == 0 ? -1 : 0;
        OnPropertyChanged(nameof(MatchPositionText));

        if (_matches.Count > 0)
            ScrollToLineRequested?.Invoke(_matches[0]);
    }

    private void ClearFind()
    {
        _matches.Clear();
        FindQuery = "";
        MatchCount = 0;
        CurrentMatchIndex = -1;
        OnPropertyChanged(nameof(MatchPositionText));
    }

    [RelayCommand]
    private void OpenInDefaultApp()
    {
        if (AddressText.Length > 0)
            OpenInDefaultAppRequested?.Invoke(AddressText);
    }

    private void CancelLoad()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void ClosePdf()
    {
        _pdfRenderer?.Dispose();
        _pdfRenderer = null;
        PageCount = 0;
        PageIndex = 0;
    }

    private void ShowPage(int pageIndex)
    {
        if (_pdfRenderer is not { } renderer || _cts is not { } cts)
            return;

        if (pageIndex < 0 || pageIndex >= renderer.PageCount)
            return;

        PageIndex = pageIndex;
        LoadCompletion = RenderPageAsync(renderer, pageIndex, cts);
    }

    private async Task RenderPageAsync(PdfPageRenderer renderer, int pageIndex, CancellationTokenSource cts)
    {
        PdfPage? page = null;
        string? failure = null;

        try
        {
            await LoadScheduler(_ => page = renderer.RenderPage(pageIndex), cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
        catch (NotSupportedException e)
        {
            failure = e.Message;
        }

        if (!ReferenceEquals(_cts, cts) || cts.IsCancellationRequested)
            return;

        if (failure is not null)
        {
            ErrorText = failure;
            return;
        }

        Image = PdfPageBitmap.From(page!);
    }

    private async Task RunLoadAsync(string fullAddress, CancellationTokenSource cts)
    {
        PreviewContent? content = null;
        PdfDocumentPreview? document = null;
        string? failure = null;

        try
        {
            await LoadScheduler(
                ct =>
                {
                    content = _loader.Load(fullAddress, ct, _limits);
                    document = OpenPdf(content);
                },
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or InvalidOperationException
            or SshException
            or SocketException
            or HostKeyChangedException)
        {
            failure = e.Message;
        }

        if (!ReferenceEquals(_cts, cts) || cts.IsCancellationRequested)
        {
            document?.Renderer.Dispose();
            return;
        }

        if (failure is not null)
        {
            document?.Renderer.Dispose();
            IsLoading = false;
            ErrorText = failure;
            return;
        }

        _pdfRenderer = document?.Renderer;
        Apply(content!, document);
    }

    private static PdfDocumentPreview? OpenPdf(PreviewContent content)
    {
        if (content.Kind != PreviewKind.Pdf || content.ImageBytes is not { } documentBytes)
            return null;

        var renderer = PdfPageRenderer.Open(documentBytes);
        if (renderer.PageCount > 0)
            return new PdfDocumentPreview(renderer, renderer.RenderPage(0));

        renderer.Dispose();
        throw new NotSupportedException("This PDF has no pages.");
    }

    private void Apply(PreviewContent content, PdfDocumentPreview? document)
    {
        Kind = content.Kind;
        EncodingLabel = content.EncodingLabel;
        SizeText = FormatUtil.HumanSize(content.TotalBytes);
        TruncationText = content.IsTruncated
            ? $"first {FormatUtil.HumanSize(content.LoadedBytes)} of {FormatUtil.HumanSize(content.TotalBytes)}"
            : "";

        if (content.Kind == PreviewKind.Image && content.ImageBytes is { } rasterBytes)
        {
            ApplyImage(rasterBytes);
            return;
        }

        if (content.Kind == PreviewKind.Vector && content.ImageBytes is { } markupBytes)
        {
            ApplyVector(markupBytes, content.Lines);
            return;
        }

        if (document is not null)
        {
            ApplyPdf(document);
            return;
        }

        FillLines(content.Lines, numbered: content.Kind == PreviewKind.Text);
        IsLoading = false;
    }

    private void ApplyPdf(PdfDocumentPreview document)
    {
        PageCount = document.Renderer.PageCount;
        PageIndex = 0;
        Image = PdfPageBitmap.From(document.FirstPage);
        IsLoading = false;
    }

    private void ApplyVector(byte[] bytes, IReadOnlyList<string> markupLines)
    {
        if (TryDecodeSvg(bytes) is { } vector)
        {
            Image = vector;
            ImageDimensionsText = $"{vector.Size.Width:0.##} × {vector.Size.Height:0.##}";
            IsLoading = false;
            return;
        }

        Kind = PreviewKind.Text;
        FillLines(markupLines, numbered: true);
        IsLoading = false;
    }

    private void ApplyImage(byte[] bytes)
    {
        if (TryDecode(bytes) is { } bitmap)
        {
            Image = bitmap;
            ImageDimensionsText = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}";
            IsLoading = false;
            return;
        }

        var shown = (int)Math.Min(_limits.TextBudgetBytes, bytes.Length);
        Kind = PreviewKind.Hex;
        FillLines(HexDump.Format(bytes.AsSpan(0, shown), 0), numbered: false);
        if (shown < bytes.Length)
            TruncationText = $"first {FormatUtil.HumanSize(shown)} of {FormatUtil.HumanSize(bytes.Length)}";
        IsLoading = false;
    }

    private static Bitmap? TryDecode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static SvgImage? TryDecodeSvg(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return SvgSource.Load(stream) is { Picture: not null } source
                ? new SvgImage { Source = source }
                : null;
        }
        catch (Exception e) when (e is XmlException
            or NullReferenceException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or FormatException
            or OverflowException)
        {
            return null;
        }
    }

    private void FillLines(IReadOnlyList<string> lines, bool numbered)
    {
        Lines.Clear();
        for (var index = 0; index < lines.Count; index++)
            Lines.Add(new PreviewLineViewModel(numbered ? index + 1 : null, lines[index]));
    }
}
