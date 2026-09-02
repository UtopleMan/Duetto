using System.Text;
using Duetto.Core.FileSystem;
using Duetto.Core.Preview;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

public class PreviewLoaderTests
{
    private static readonly PreviewLimits TinyLimits = new()
    {
        TextBudgetBytes = 32,
        ImageMaxBytes = 64,
        SniffBytes = 8,
    };

    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    ];

    private static PreviewLoader LocalLoader() => new(new FileSystemRegistry());

    private static string WriteBytes(TempDir tmp, string name, byte[] bytes)
    {
        var path = tmp.File(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Utf8_text_file_loads_as_text_lines()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "first\nsecond\nthird\n");

        var content = LocalLoader().Load(path, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, content.Kind);
        Assert.Equal(["first", "second", "third"], content.Lines);
        Assert.Equal("UTF-8", content.EncodingLabel);
        Assert.False(content.IsTruncated);
        Assert.Equal(content.TotalBytes, content.LoadedBytes);
    }

    [Fact]
    public void Crlf_line_endings_are_normalised()
    {
        using var tmp = new TempDir();
        var path = tmp.File("crlf.txt", "one\r\ntwo\r\n");

        var content = LocalLoader().Load(path, CancellationToken.None);

        Assert.Equal(["one", "two"], content.Lines);
    }

    [Fact]
    public void Utf8_bom_is_stripped_from_the_first_line()
    {
        using var tmp = new TempDir();
        var path = WriteBytes(tmp, "bom.txt", [.. new UTF8Encoding(true).GetPreamble(), .. "hello\n"u8]);

        var content = LocalLoader().Load(path, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, content.Kind);
        Assert.Equal("UTF-8 (BOM)", content.EncodingLabel);
        Assert.Equal(["hello"], content.Lines);
    }

    [Fact]
    public void Utf16_le_file_decodes_without_the_bom()
    {
        using var tmp = new TempDir();
        var path = WriteBytes(tmp, "utf16.txt", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("alpha\nbeta\n")]);

        var content = LocalLoader().Load(path, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, content.Kind);
        Assert.Equal("UTF-16 LE", content.EncodingLabel);
        Assert.Equal(["alpha", "beta"], content.Lines);
    }

    [Fact]
    public void Embedded_nul_produces_a_hex_dump()
    {
        using var tmp = new TempDir();
        var path = WriteBytes(tmp, "binary.bin", [0x61, 0x62, 0x00, 0x63]);

        var content = LocalLoader().Load(path, CancellationToken.None);

        Assert.Equal(PreviewKind.Hex, content.Kind);
        Assert.Equal(
            "00000000  61 62 00 63                                       |ab.c|",
            Assert.Single(content.Lines));
    }

    [Fact]
    public void Png_under_the_cap_loads_every_byte()
    {
        using var tmp = new TempDir();
        var path = WriteBytes(tmp, "pixel.png", PngBytes);

        var content = LocalLoader().Load(path, CancellationToken.None);

        Assert.Equal(PreviewKind.Image, content.Kind);
        Assert.Equal(PngBytes, content.ImageBytes);
        Assert.Empty(content.Lines);
        Assert.False(content.IsTruncated);
    }

    [Fact]
    public void Png_over_the_image_cap_falls_back_to_hex()
    {
        using var tmp = new TempDir();
        var path = WriteBytes(tmp, "big.png", [.. PngBytes, .. new byte[TinyLimits.ImageMaxBytes]]);

        var content = LocalLoader().Load(path, CancellationToken.None, TinyLimits);

        Assert.Equal(PreviewKind.Hex, content.Kind);
        Assert.Null(content.ImageBytes);
    }

    [Fact]
    public void File_beyond_the_text_budget_is_truncated()
    {
        using var tmp = new TempDir();
        var path = tmp.File("long.txt", new string('x', 100));

        var content = LocalLoader().Load(path, CancellationToken.None, TinyLimits);

        Assert.True(content.IsTruncated);
        Assert.Equal(TinyLimits.TextBudgetBytes, content.LoadedBytes);
        Assert.Equal(100, content.TotalBytes);
        Assert.Equal(new string('x', 32), Assert.Single(content.Lines));
    }

    [Fact]
    public void File_exactly_at_the_text_budget_is_not_truncated()
    {
        using var tmp = new TempDir();
        var path = tmp.File("exact.txt", new string('y', (int)TinyLimits.TextBudgetBytes));

        var content = LocalLoader().Load(path, CancellationToken.None, TinyLimits);

        Assert.False(content.IsTruncated);
        Assert.Equal(TinyLimits.TextBudgetBytes, content.LoadedBytes);
    }

    [Fact]
    public void Empty_file_is_the_empty_kind()
    {
        using var tmp = new TempDir();
        var path = tmp.File("nothing.txt");

        var content = LocalLoader().Load(path, CancellationToken.None);

        Assert.Equal(PreviewKind.Empty, content.Kind);
        Assert.Empty(content.Lines);
        Assert.Equal(0, content.TotalBytes);
        Assert.False(content.IsTruncated);
    }

    [Fact]
    public void Remote_address_loads_through_the_registry()
    {
        var remote = new InMemoryFileSystemProvider();
        remote.CreateFile("/", "note.txt");
        using (var stream = remote.OpenWrite("/note.txt"))
            stream.Write("remote line\n"u8);

        var registry = new FileSystemRegistry();
        registry.Register("sftp", "srv", remote);

        var content = new PreviewLoader(registry).Load("sftp://srv/note.txt", CancellationToken.None);

        Assert.Equal(PreviewKind.Text, content.Kind);
        Assert.Equal(["remote line"], content.Lines);
    }

    [Fact]
    public void Cancelled_token_throws_before_any_read()
    {
        using var tmp = new TempDir();
        var path = tmp.File("note.txt", "content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => LocalLoader().Load(path, cts.Token));
    }

    [Fact]
    public void Missing_file_throws_file_not_found()
    {
        using var tmp = new TempDir();
        var missing = Path.Combine(tmp.Path, "gone.txt");

        Assert.Throws<FileNotFoundException>(() => LocalLoader().Load(missing, CancellationToken.None));
    }

    [Fact]
    public void Directory_is_not_previewable()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("folder");

        Assert.Throws<NotSupportedException>(() => LocalLoader().Load(dir, CancellationToken.None));
    }
}
