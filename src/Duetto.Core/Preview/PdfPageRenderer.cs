using Docnet.Core;
using Docnet.Core.Exceptions;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace Duetto.Core.Preview;

public sealed class PdfPageRenderer : IDisposable
{
    internal const int MaxEdgePixels = 2400;

    private const uint PdfiumPasswordError = 4;

    private static readonly Lock pdfiumGate = new();

    private readonly IDocReader document;

    private bool isDisposed;

    private PdfPageRenderer(IDocReader openedDocument)
    {
        document = openedDocument;
        PageCount = openedDocument.GetPageCount();
    }

    public int PageCount { get; }

    public static PdfPageRenderer Open(byte[] documentBytes)
    {
        lock (pdfiumGate)
        {
            return new PdfPageRenderer(OpenDocument(documentBytes));
        }
    }

    public PdfPage RenderPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);

        lock (pdfiumGate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return Render(pageIndex);
        }
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        lock (pdfiumGate)
        {
            document.Dispose();
        }
    }

    private PdfPage Render(int pageIndex)
    {
        try
        {
            using var page = document.GetPageReader(pageIndex);
            return new PdfPage(page.GetImage(), page.GetPageWidth(), page.GetPageHeight());
        }
        catch (DocnetException e)
        {
            throw new NotSupportedException($"Page {pageIndex + 1} of this PDF cannot be rendered.", e);
        }
    }

    private static IDocReader OpenDocument(byte[] documentBytes)
    {
        try
        {
            return DocLib.Instance.GetDocReader(
                documentBytes,
                new PageDimensions(MaxEdgePixels, MaxEdgePixels));
        }
        catch (Exception e) when (e is DocnetException or ArgumentException)
        {
            throw new NotSupportedException(UnreadableReason(e), e);
        }
    }

    private static string UnreadableReason(Exception failure) =>
        failure is DocnetLoadDocumentException { ErrorCode: PdfiumPasswordError }
            ? "This PDF is password-protected and cannot be previewed."
            : "This PDF cannot be rendered - the file is damaged or not a PDF.";
}
