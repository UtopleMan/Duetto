using Duetto.Core.Preview;

namespace Duetto.Tests.Core;

public class PdfPageRendererTests
{
    internal static byte[] TwoPageDocument() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "two-pages.pdf"));

    [Fact]
    public void Page_count_matches_the_document()
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        Assert.Equal(2, renderer.PageCount);
    }

    [Fact]
    public void Rendered_page_carries_four_bytes_per_pixel()
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        var page = renderer.RenderPage(0);

        Assert.Equal(page.Width * page.Height * 4, page.Pixels.Length);
    }

    [Fact]
    public void Rendered_page_keeps_the_source_aspect_ratio()
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        var page = renderer.RenderPage(0);

        Assert.Equal(2d, (double)page.Width / page.Height, precision: 2);
    }

    [Fact]
    public void Rendered_page_is_capped_on_its_long_edge()
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        var page = renderer.RenderPage(0);

        Assert.Equal(PdfPageRenderer.MaxEdgePixels, Math.Max(page.Width, page.Height));
    }

    [Fact]
    public void Rendered_page_paints_its_content_rather_than_a_blank_sheet()
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        var page = renderer.RenderPage(0);

        Assert.True(page.Pixels.Distinct().Count() > 1);
    }

    [Fact]
    public void Rendered_pixels_are_blue_green_red_alpha_ordered()
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        var page = renderer.RenderPage(0);

        var centre = ((page.Height / 2 * page.Width) + (page.Width / 2)) * 4;
        Assert.Equal<byte>([0xFF, 0x00, 0x00, 0xFF], page.Pixels[centre..(centre + 4)]);
    }

    [Fact]
    public void Each_page_renders_its_own_content()
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        var first = renderer.RenderPage(0);
        var second = renderer.RenderPage(1);

        Assert.NotEqual(first.Pixels, second.Pixels);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Page_index_outside_the_document_is_rejected(int pageIndex)
    {
        using var renderer = PdfPageRenderer.Open(TwoPageDocument());

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderPage(pageIndex));
    }

    [Fact]
    public void Corrupt_document_is_reported_rather_than_crashing()
    {
        var failure = Assert.Throws<NotSupportedException>(
            () => PdfPageRenderer.Open("%PDF-1.4\nnot really a document"u8.ToArray()));

        Assert.Contains("damaged", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Truncated_document_is_reported_rather_than_crashing()
    {
        var truncated = TwoPageDocument()[..300];

        Assert.Throws<NotSupportedException>(() => PdfPageRenderer.Open(truncated));
    }

    [Fact]
    public void Empty_document_is_reported_rather_than_crashing()
    {
        Assert.Throws<NotSupportedException>(() => PdfPageRenderer.Open([]));
    }

    [Fact]
    public void Rendering_after_dispose_is_rejected()
    {
        var renderer = PdfPageRenderer.Open(TwoPageDocument());
        renderer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => renderer.RenderPage(0));
    }
}
