using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Duetto.Core.FileSystem;
using Duetto.Core.Preview;
using Duetto.Tests.Core;
using Duetto.Tests.Support;
using Duetto.ViewModels;
using Duetto.Views;

namespace Duetto.Tests.Ui;

public class ViewerTests
{
    internal static readonly byte[] TwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP4zwAE/xkgFAAb8gP91pbyKwAAAABJRU5ErkJggg==");

    private static readonly PreviewLimits TinyLimits = new()
    {
        TextBudgetBytes = 32,
        ImageMaxBytes = 64,
        PdfMaxBytes = 512,
        SniffBytes = 8,
    };

    internal static ViewerViewModel Viewer(FileSystemRegistry? registry = null, PreviewLimits? limits = null)
    {
        var vm = new ViewerViewModel(registry ?? new FileSystemRegistry(), limits)
        {
            LoadScheduler = (work, ct) => { work(ct); return Task.CompletedTask; },
        };
        return vm;
    }

    internal static string WriteBytes(TempDir tmp, string name, byte[] bytes)
    {
        var path = tmp.File(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal static IEnumerable<byte> CentrePixelChannelsAscending(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var offset = (buffer.RowBytes * (frame.PixelSize.Height / 2)) + (frame.PixelSize.Width / 2 * 4);
        var pixel = new byte[4];
        Marshal.Copy(buffer.Address + offset, pixel, 0, pixel.Length);
        return pixel[..3].Order();
    }

    [AvaloniaFact]
    public void Text_file_fills_numbered_lines()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "alpha\nbeta\n");
        var vm = Viewer();

        vm.Show(path, "note.txt");

        Assert.False(vm.IsLoading);
        Assert.Equal(PreviewKind.Text, vm.Kind);
        Assert.Equal(["alpha", "beta"], vm.Lines.Select(l => l.Text));
        Assert.Equal([1, 2], vm.Lines.Select(l => l.Number));
        Assert.Equal("UTF-8", vm.EncodingLabel);
        Assert.True(vm.ShowLineNumbers);
        Assert.True(vm.IsTextMode);
        Assert.Contains("note.txt", vm.HeaderText);
        Assert.Contains("UTF-8", vm.HeaderText);
    }

    [AvaloniaFact]
    public void Binary_file_yields_unnumbered_hex_rows()
    {
        using var tmp = new TempDir();
        var path = WriteBytes(tmp, "blob.bin", [0x00, 0x01, 0x02, 0x03]);
        var vm = Viewer();

        vm.Show(path, "blob.bin");

        Assert.Equal(PreviewKind.Hex, vm.Kind);
        Assert.All(vm.Lines, line => Assert.Null(line.Number));
        Assert.StartsWith("00000000  00 01 02 03", Assert.Single(vm.Lines).Text);
        Assert.False(vm.ShowLineNumbers);
        Assert.Contains("Binary", vm.HeaderText);
    }

    [AvaloniaFact]
    public void Png_sets_the_image_and_its_dimensions()
    {
        using var tmp = new TempDir();
        var path = WriteBytes(tmp, "pixel.png", TwoByTwoPng);
        var vm = Viewer();

        vm.Show(path, "pixel.png");

        Assert.Equal(PreviewKind.Image, vm.Kind);
        Assert.NotNull(vm.Image);
        Assert.Equal("2 × 2", vm.ImageDimensionsText);
        Assert.True(vm.IsImageMode);
        Assert.False(vm.IsTextMode);
        Assert.Empty(vm.Lines);
    }

    [AvaloniaFact]
    public void Undecodable_image_falls_back_to_hex_rather_than_an_error()
    {
        using var tmp = new TempDir();
        var truncated = TwoByTwoPng[..20];
        var path = WriteBytes(tmp, "broken.png", truncated);
        var vm = Viewer();

        vm.Show(path, "broken.png");

        Assert.Equal(PreviewKind.Hex, vm.Kind);
        Assert.False(vm.HasError);
        Assert.Null(vm.Image);
        Assert.NotEmpty(vm.Lines);
    }

    [AvaloniaFact]
    public void Svg_sets_the_image_and_its_viewbox_dimensions()
    {
        using var tmp = new TempDir();
        var path = tmp.File("logo.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="120" height="60" viewBox="0 0 120 60">
              <rect width="120" height="60" fill="#3060c0" />
            </svg>
            """);
        var vm = Viewer();

        vm.Show(path, "logo.svg");

        Assert.Equal(PreviewKind.Vector, vm.Kind);
        Assert.NotNull(vm.Image);
        Assert.Equal("120 × 60", vm.ImageDimensionsText);
        Assert.True(vm.IsVectorMode);
        Assert.False(vm.IsTextMode);
        Assert.Contains("SVG", vm.HeaderText);
    }

    [AvaloniaFact]
    public void Malformed_svg_falls_back_to_text_rather_than_an_error()
    {
        using var tmp = new TempDir();
        var path = tmp.File("broken.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect ");
        var vm = Viewer();

        vm.Show(path, "broken.svg");

        Assert.Equal(PreviewKind.Text, vm.Kind);
        Assert.False(vm.HasError);
        Assert.Null(vm.Image);
        Assert.Equal(["<svg xmlns=\"http://www.w3.org/2000/svg\"><rect "], vm.Lines.Select(l => l.Text));
    }

    [AvaloniaFact]
    public void Truncated_file_reports_loaded_and_total_size()
    {
        using var tmp = new TempDir();
        var path = tmp.File("long.txt", new string('x', 100));
        var vm = Viewer(limits: TinyLimits);

        vm.Show(path, "long.txt");

        Assert.True(vm.HasTruncation);
        Assert.Equal("first 32 B of 100 B", vm.TruncationText);
    }

    [AvaloniaFact]
    public void Empty_file_shows_the_empty_state()
    {
        using var tmp = new TempDir();
        var path = tmp.File("nothing.txt");
        var vm = Viewer();

        vm.Show(path, "nothing.txt");

        Assert.Equal(PreviewKind.Empty, vm.Kind);
        Assert.True(vm.IsEmptyFile);
        Assert.False(vm.IsTextMode);
        Assert.Empty(vm.Lines);
    }

    [AvaloniaFact]
    public void Unreadable_path_sets_the_error_and_stops_loading()
    {
        using var tmp = new TempDir();
        var vm = Viewer();

        vm.Show(Path.Combine(tmp.Path, "gone.txt"), "gone.txt");

        Assert.True(vm.HasError);
        Assert.NotEmpty(vm.ErrorText);
        Assert.False(vm.IsLoading);
        Assert.False(vm.IsTextMode);
    }

    [AvaloniaFact]
    public void Second_show_replaces_the_first_file_content()
    {
        using var tmp = new TempDir();
        var first = tmp.File("first.txt", "one\ntwo\nthree\n");
        var second = tmp.File("second.txt", "only\n");
        var vm = Viewer();

        vm.Show(first, "first.txt");
        vm.Show(second, "second.txt");

        Assert.Equal(["only"], vm.Lines.Select(l => l.Text));
        Assert.Equal("second.txt", vm.FileName);
        Assert.False(vm.HasTruncation);
    }

    [AvaloniaFact]
    public void Image_then_text_clears_the_bitmap()
    {
        using var tmp = new TempDir();
        var png = WriteBytes(tmp, "pixel.png", TwoByTwoPng);
        var text = tmp.File("note.txt", "hello\n");
        var vm = Viewer();

        vm.Show(png, "pixel.png");
        vm.Show(text, "note.txt");

        Assert.Null(vm.Image);
        Assert.Equal("", vm.ImageDimensionsText);
        Assert.Equal(PreviewKind.Text, vm.Kind);
    }

    [AvaloniaFact]
    public void Open_in_default_app_raises_the_request_with_the_address()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "x");
        var vm = Viewer();
        string? requested = null;
        vm.OpenInDefaultAppRequested += address => requested = address;

        vm.Show(path, "note.txt");
        vm.OpenInDefaultAppCommand.Execute(null);

        Assert.Equal(path, requested);
    }

    [AvaloniaFact]
    public void Remote_address_loads_through_the_registry()
    {
        var remote = new InMemoryFileSystemProvider();
        remote.CreateFile("/", "note.txt");
        using (var stream = remote.OpenWrite("/note.txt"))
            stream.Write("remote\n"u8);

        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv", remote);
        var vm = Viewer(registry);

        vm.Show("sftp://srv/note.txt", "note.txt");

        Assert.Equal(["remote"], vm.Lines.Select(l => l.Text));
    }

    [AvaloniaFact]
    public void Window_shows_the_view_model_content()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "alpha\n");
        var vm = Viewer();
        var window = new ViewerWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Show(path, "note.txt");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("note.txt", window.Title);
        Assert.True(window.FindControl<ListBox>("LineList")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Svg_actually_paints_into_the_window()
    {
        using var tmp = new TempDir();
        var path = tmp.File("fill.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="120" height="60" viewBox="0 0 120 60">
              <rect width="120" height="60" fill="#3060c0" />
            </svg>
            """);
        var vm = Viewer();
        var window = new ViewerWindow(vm);
        window.Show();
        vm.Show(path, "fill.svg");
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var channels = CentrePixelChannelsAscending(frame);
        window.Close();

        Assert.Equal<byte>([0x30, 0x60, 0xC0], channels);
    }

    [AvaloniaFact]
    public void Escape_closes_the_window()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "alpha\n");
        var vm = Viewer();
        var window = new ViewerWindow(vm);
        window.Show();
        vm.Show(path, "note.txt");
        Dispatcher.UIThread.RunJobs();

        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
    }
}
