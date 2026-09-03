using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Duetto.Core.Preview;
using Duetto.Tests.Core;
using Duetto.Views;

namespace Duetto.Tests.Ui;

public class ViewerPdfTests
{
    private static string WritePdf(TempDir tmp, string name = "pages.pdf") =>
        ViewerTests.WriteBytes(tmp, name, PdfPageRendererTests.TwoPageDocument());

    [AvaloniaFact]
    public void Pdf_renders_its_first_page()
    {
        using var tmp = new TempDir();
        var vm = ViewerTests.Viewer();

        vm.Show(WritePdf(tmp), "pages.pdf");

        Assert.Equal(PreviewKind.Pdf, vm.Kind);
        Assert.True(vm.IsPdfMode);
        Assert.False(vm.IsTextMode);
        Assert.NotNull(vm.Image);
        Assert.Equal(2, vm.PageCount);
        Assert.Equal(0, vm.PageIndex);
        Assert.Equal("1 / 2", vm.PageText);
        Assert.Contains("PDF", vm.HeaderText);
    }

    [AvaloniaFact]
    public void Next_page_advances_and_re_renders()
    {
        using var tmp = new TempDir();
        var vm = ViewerTests.Viewer();
        vm.Show(WritePdf(tmp), "pages.pdf");
        var firstPage = vm.Image;

        vm.NextPage();

        Assert.Equal(1, vm.PageIndex);
        Assert.Equal("2 / 2", vm.PageText);
        Assert.NotSame(firstPage, vm.Image);
    }

    [AvaloniaFact]
    public void Navigation_clamps_at_the_last_page()
    {
        using var tmp = new TempDir();
        var vm = ViewerTests.Viewer();
        vm.Show(WritePdf(tmp), "pages.pdf");

        vm.NextPage();
        vm.NextPage();

        Assert.Equal(1, vm.PageIndex);
    }

    [AvaloniaFact]
    public void Navigation_clamps_at_the_first_page()
    {
        using var tmp = new TempDir();
        var vm = ViewerTests.Viewer();
        vm.Show(WritePdf(tmp), "pages.pdf");

        vm.PreviousPage();

        Assert.Equal(0, vm.PageIndex);
        Assert.False(vm.HasError);
    }

    [AvaloniaFact]
    public void Find_and_wrap_stay_hidden_in_pdf_mode()
    {
        using var tmp = new TempDir();
        var vm = ViewerTests.Viewer();
        vm.Show(WritePdf(tmp), "pages.pdf");

        vm.OpenFind();

        Assert.False(vm.IsFindOpen);
        Assert.False(vm.IsFindVisible);
        Assert.Contains("PgUp", vm.FooterHintText);
    }

    [AvaloniaFact]
    public void Corrupt_pdf_reports_a_reason_rather_than_a_hex_dump()
    {
        using var tmp = new TempDir();
        var path = ViewerTests.WriteBytes(tmp, "broken.pdf", "%PDF-1.4\nshredded"u8.ToArray());
        var vm = ViewerTests.Viewer();

        vm.Show(path, "broken.pdf");

        Assert.True(vm.HasError);
        Assert.Contains("damaged", vm.ErrorText, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsTextMode);
        Assert.Empty(vm.Lines);
    }

    [AvaloniaFact]
    public void Pdf_then_text_drops_the_page_state()
    {
        using var tmp = new TempDir();
        var pdf = WritePdf(tmp);
        var text = tmp.File("note.txt", "hello\n");
        var vm = ViewerTests.Viewer();

        vm.Show(pdf, "pages.pdf");
        vm.Show(text, "note.txt");

        Assert.Equal(0, vm.PageCount);
        Assert.Equal("", vm.PageText);
        Assert.Null(vm.Image);
        Assert.False(vm.IsPdfMode);
    }

    [AvaloniaFact]
    public void Page_keys_step_through_the_document()
    {
        using var tmp = new TempDir();
        var vm = ViewerTests.Viewer();
        var window = new ViewerWindow(vm);
        window.Show();
        vm.Show(WritePdf(tmp), "pages.pdf");
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterPageDown = vm.PageIndex;

        window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.Close();

        Assert.Equal(1, afterPageDown);
        Assert.Equal(0, vm.PageIndex);
    }

    [AvaloniaFact]
    public void Pdf_page_actually_paints_into_the_window()
    {
        using var tmp = new TempDir();
        var vm = ViewerTests.Viewer();
        var window = new ViewerWindow(vm);
        window.Show();
        vm.Show(WritePdf(tmp), "pages.pdf");
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var channels = ViewerTests.CentrePixelChannelsAscending(frame);
        window.Close();

        Assert.Equal<byte>([0x00, 0x00, 0xFF], channels);
    }
}
